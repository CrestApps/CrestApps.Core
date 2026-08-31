using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SQLitePCL;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// A file-backed SQLite database that holds the tabular files for a conversation scope. The workspace
/// is built lazily on the first tabular tool call: once the table is created in the on-disk database
/// file it persists for the lifetime of the owning session without requiring explicit snapshotting.
/// Mutations (added or removed columns, updated values, inserted or deleted rows) are applied to this
/// copy and written through to disk automatically by SQLite; the originally uploaded file is never
/// modified.
/// </summary>
internal sealed class TabularWorkspace : IDisposable
{
    private const string MetadataTableName = "_workspace_meta";
    private const int ImportProgressIntervalRows = 250;

    // SQLite SQLITE_DBCONFIG_DQS_* op codes. Used to re-enable the legacy double-quoted string
    // literal fallback (for both DML and DDL statements) that Microsoft.Data.Sqlite disables by
    // default.
    private const int SqliteDbConfigDqsDml = 1013;
    private const int SqliteDbConfigDqsDdl = 1014;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TabularWorkspaceOptions _options;
    private readonly string _databasePath;
    private readonly ILogger<TabularWorkspace> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, LoadedTable> _tables = new(StringComparer.Ordinal);
    private int _mutationVersion;
    private SqliteConnection _connection;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabularWorkspace"/> class.
    /// </summary>
    /// <param name="options">The workspace options.</param>
    /// <param name="databasePath">
    /// The absolute path to the SQLite database file. When <see langword="null"/> or empty, an
    /// in-memory database is used (primarily for unit tests).
    /// </param>
    public TabularWorkspace(
        TabularWorkspaceOptions options,
        string databasePath = null,
        ILogger<TabularWorkspace> logger = null)
    {
        _options = options;
        _databasePath = databasePath;
        _logger = logger ?? NullLogger<TabularWorkspace>.Instance;
    }

    /// <summary>
    /// Ensures the database is built and contains a table for each supplied document. Loading is
    /// lazy: <paramref name="contentLoader"/> is only invoked for documents that do not yet have a
    /// table. Calling this multiple times within the same prompt reuses the already-built tables
    /// rather than recreating them.
    /// </summary>
    /// <param name="documents">The tabular documents that should be available in the workspace.</param>
    /// <param name="contentLoader">A delegate that loads the raw tabular content for a document id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tables available in the workspace after synchronization.</returns>
    public async Task<IReadOnlyList<TabularTableInfo>> EnsureReadyAsync(
        IReadOnlyList<TabularDocumentRef> documents,
        Func<string, CancellationToken, Task<string>> contentLoader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentLoader);

        return await EnsureReadyAsync(
            documents,
            async (document, token) => TabularDocumentArtifact.FromDelimitedContent(
                await contentLoader(document.DocumentId, token),
                document.FileName),
            workspaceImporter: null,
            cancellationToken);
    }

    /// <summary>
    /// Ensures the database is built and contains a table for each supplied document. Loading is
    /// lazy: <paramref name="artifactLoader"/> is only invoked for documents that do not yet have a
    /// table. Calling this multiple times within the same workspace reuses the already-built tables
    /// rather than recreating them.
    /// </summary>
    /// <param name="documents">The tabular documents that should be available in the workspace.</param>
    /// <param name="artifactLoader">A delegate that loads the parsed tabular artifact for a document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tables available in the workspace after synchronization.</returns>
    public async Task<IReadOnlyList<TabularTableInfo>> EnsureReadyAsync(
        IReadOnlyList<TabularDocumentRef> documents,
        Func<TabularDocumentRef, CancellationToken, Task<TabularDocumentArtifact>> artifactLoader,
        Func<TabularDocumentRef, SqliteConnection, TabularTableNameAllocator, CancellationToken, Task<IReadOnlyList<TabularWorkspaceImportResult>>> workspaceImporter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(artifactLoader);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            _connection ??= OpenConnection();

            if (_tables.Count == 0)
            {
                LoadMetadataFromDatabase();
            }

            // Building tables writes to the database, so open a write window for the duration of the
            // synchronization and close it again afterward.
            SetWritable(_connection, true);

            try
            {
                await SynchronizeTablesAsync(documents, artifactLoader, workspaceImporter, cancellationToken);
            }
            finally
            {
                SetWritable(_connection, false);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Tabular workspace ready with {LoadedTableCount} loaded table(s) for {RequestedDocumentCount} requested document(s) in {ElapsedMilliseconds} ms.",
                    _tables.Count,
                    documents.Count,
                    stopwatch.ElapsedMilliseconds);
            }

            return BuildTableInfos();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Gets the tables currently loaded in the workspace, including their schema and row counts.
    /// Returns an empty list when the database has not been built yet.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The loaded tables, or an empty list when nothing is loaded.</returns>
    public async Task<IReadOnlyList<TabularTableInfo>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_connection is null)
            {
                return [];
            }

            return BuildTableInfos();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs a read-only SQL query (a single <c>SELECT</c> or <c>WITH … SELECT</c> statement) against
    /// the in-memory database and returns up to <paramref name="maxRows"/> rows.
    /// </summary>
    /// <param name="sql">The read-only SQL query.</param>
    /// <param name="maxRows">The maximum number of rows to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The query result.</returns>
    public async Task<TabularQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken = default)
    {
        var statement = TabularSqlGuard.EnsureReadOnlyQuery(sql);

        var limit = maxRows <= 0 || maxRows > _options.MaxRowsPerQuery
            ? _options.MaxRowsPerQuery
            : maxRows;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            EnsureLoaded();

            using var command = _connection.CreateCommand();
            command.CommandText = statement;
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columns = new string[reader.FieldCount];

            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns[i] = reader.GetName(i);
            }

            var rows = new List<object[]>();
            var truncated = false;

            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count >= limit)
                {
                    truncated = true;

                    break;
                }

                var row = new object[reader.FieldCount];

                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : TruncateValue(reader.GetValue(i));
                }

                rows.Add(row);
            }

            return new TabularQueryResult
            {
                Columns = columns,
                Rows = rows,
                Truncated = truncated,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs one or more data-manipulation or schema statements against the in-memory database in a
    /// single transaction. All statements are applied as one batch so the model can make every change
    /// in a single tool call instead of many round-trips. The changes apply only to the in-memory copy
    /// and are discarded when the prompt completes. If any statement fails, the whole batch is rolled
    /// back.
    /// </summary>
    /// <param name="sql">One or more manipulation or schema statements separated by semicolons.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The command result.</returns>
    public async Task<TabularCommandResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        var statements = TabularSqlGuard.EnsureCommandBatch(sql);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            EnsureLoaded();

            // Manipulation statements write, so open a write window for the batch and close it again
            // once the transaction has committed or rolled back.
            SetWritable(_connection, true);

            try
            {
                var affected = 0;

                using var transaction = _connection.BeginTransaction();

                try
                {
                    foreach (var statement in statements)
                    {
                        using var command = _connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = statement;
                        command.CommandTimeout = _options.CommandTimeoutSeconds;

                        affected += await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    Interlocked.Increment(ref _mutationVersion);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);

                    throw;
                }

                return new TabularCommandResult(affected, statements.Count);
            }
            finally
            {
                SetWritable(_connection, false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public int MutationVersion => Volatile.Read(ref _mutationVersion);

    /// <summary>
    /// Writes the result of a single read-only SQL query to <paramref name="destination"/> as CSV.
    /// The query can only read from the already-loaded in-memory tabular workspace.
    /// </summary>
    /// <param name="sql">The read-only SQL query to export.</param>
    /// <param name="destination">The destination stream that receives CSV content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export result.</returns>
    public async Task<TabularExportResult> ExportCsvAsync(
        string sql,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var statement = TabularSqlGuard.EnsureReadOnlyQuery(sql);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            EnsureLoaded();

            using var command = _connection.CreateCommand();
            command.CommandText = statement;
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columns = new string[reader.FieldCount];

            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns[i] = reader.GetName(i);
            }

            var rows = new List<List<string>>();

            using (var writer = new StreamWriter(
                destination,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true))
            {
                await WriteCsvRowAsync(writer, columns, cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    if (_options.MaxRowsPerExport > 0 && rows.Count >= _options.MaxRowsPerExport)
                    {
                        throw new TabularSqlException($"The export exceeds the configured limit of {_options.MaxRowsPerExport} rows. Refine the query before exporting.");
                    }

                    var row = new string[reader.FieldCount];

                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = reader.IsDBNull(i)
                            ? string.Empty
                            : FormatExportValue(reader.GetValue(i));
                    }

                    rows.Add(row.ToList());
                    await WriteCsvRowAsync(writer, row, cancellationToken);
                }

                await writer.FlushAsync(cancellationToken);
            }

            return new TabularExportResult(
                rows.Count,
                new TabularDocumentArtifact
                {
                    Header = columns.ToList(),
                    Rows = rows,
                });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Executes a read-only query and returns the result as an in-memory artifact (header and rows)
    /// without writing to any specific file format. Callers pair this with a file writer to produce a
    /// downloadable export in the desired format. The query can only read from the already-loaded
    /// in-memory tabular workspace.
    /// </summary>
    /// <param name="sql">The read-only SQL query to export.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export result.</returns>
    public async Task<TabularExportResult> ExportAsync(
        string sql,
        CancellationToken cancellationToken = default)
    {
        var statement = TabularSqlGuard.EnsureReadOnlyQuery(sql);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            EnsureLoaded();

            using var command = _connection.CreateCommand();
            command.CommandText = statement;
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            return await ReadExportAsync(command, mapHeader: null, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Exports the complete, current contents of the in-memory tabular workspace, including every
    /// in-memory manipulation applied so far (added or removed columns, updated values, inserted or
    /// deleted rows). The export reflects the live in-memory data rather than the originally uploaded
    /// file, and the header row uses the original source column names where available. This is the
    /// export used when the user asks for "the file" with the updated data.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export result for the full current table.</returns>
    public async Task<TabularExportResult> ExportFullAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            EnsureLoaded();

            if (_tables.Count == 0)
            {
                throw new TabularSqlException("There is no tabular data loaded to export.");
            }

            if (_tables.Count > 1)
            {
                throw new TabularSqlException("Multiple tabular tables are loaded. Provide an explicit SELECT query to choose what to export.");
            }

            var table = _tables.Values.First();

            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {QuoteIdentifier(table.TableName)}";
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            return await ReadExportAsync(
                command,
                sqlName => table.SourceNames.TryGetValue(sqlName, out var sourceName) && !string.IsNullOrEmpty(sourceName)
                    ? sourceName
                    : sqlName,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TabularExportResult> ReadExportAsync(
        SqliteCommand command,
        Func<string, string> mapHeader,
        CancellationToken cancellationToken)
    {
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = new List<string>(reader.FieldCount);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            columns.Add(mapHeader is null
                ? name
                : mapHeader(name));
        }

        var rows = new List<List<string>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            if (_options.MaxRowsPerExport > 0 && rows.Count >= _options.MaxRowsPerExport)
            {
                throw new TabularSqlException($"The export exceeds the configured limit of {_options.MaxRowsPerExport} rows. Refine the query before exporting.");
            }

            var row = new List<string>(reader.FieldCount);

            for (var i = 0; i < reader.FieldCount; i++)
            {
                row.Add(reader.IsDBNull(i)
                    ? string.Empty
                    : FormatExportValue(reader.GetValue(i)));
            }

            rows.Add(row);
        }

        return new TabularExportResult(
            rows.Count,
            new TabularDocumentArtifact
            {
                Header = columns,
                Rows = rows,
            });
    }

    /// <summary>
    /// Disposes the database connection and releases the concurrency gate.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _gate.Wait();

        try
        {
            _connection?.Dispose();
            _connection = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private void EnsureLoaded()
    {
        if (_connection is null)
        {
            throw new TabularSqlException("The tabular workspace is not loaded. Load the data before querying it.");
        }
    }

    /// <summary>
    /// Toggles SQLite's connection-level <c>query_only</c> flag. The workspace keeps the connection
    /// read-only by default so the database engine itself rejects any write, and only the table-build
    /// and manipulation paths open a narrow write window. This is defense in depth beyond the SQL text
    /// validation in <see cref="TabularSqlGuard"/>: a write that slipped past the parser on a read path
    /// still cannot modify data, because the engine refuses it. Every workspace operation runs under
    /// <see cref="_gate"/>, so this flag is never observed by a concurrent operation.
    /// </summary>
    /// <param name="connection">The connection to toggle.</param>
    /// <param name="writable">When <see langword="true"/>, writes are allowed; otherwise they are blocked.</param>
    private static void SetWritable(SqliteConnection connection, bool writable)
    {
        if (connection is null)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = writable ? "PRAGMA query_only = OFF" : "PRAGMA query_only = ON";
        command.ExecuteNonQuery();
    }

    private async Task SynchronizeTablesAsync(
        IReadOnlyList<TabularDocumentRef> documents,
        Func<TabularDocumentRef, CancellationToken, Task<TabularDocumentArtifact>> artifactLoader,
        Func<TabularDocumentRef, SqliteConnection, TabularTableNameAllocator, CancellationToken, Task<IReadOnlyList<TabularWorkspaceImportResult>>> workspaceImporter,
        CancellationToken cancellationToken)
    {
        var usedNames = new HashSet<string>(_tables.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            if (IsDocumentLoaded(document.DocumentId))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Skipping tabular document '{DocumentId}' because it is already loaded.",
                        document.DocumentId);
                }

                continue;
            }

            var importStopwatch = Stopwatch.StartNew();

            if (workspaceImporter != null)
            {
                TabularTableNameAllocator allocator = (worksheetName, singleWorksheet) =>
                    AllocateTableName(usedNames, document.FileName, worksheetName, singleWorksheet);

                var importResults = await workspaceImporter(document, _connection, allocator, cancellationToken);

                if (importResults != null)
                {
                    foreach (var importResult in importResults)
                    {
                        RegisterTable(document, importResult.TableName, importResult.WorksheetName, importResult.Columns);
                    }

                    importStopwatch.Stop();

                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Loaded tabular document '{FileName}' into {TableCount} table(s) via streaming importer in {ImportMilliseconds} ms.",
                            document.FileName,
                            importResults.Count,
                            importStopwatch.ElapsedMilliseconds);
                    }

                    continue;
                }
            }

            var artifact = await artifactLoader(document, cancellationToken);
            var worksheets = artifact?.GetWorksheets() ?? [new TabularWorksheet()];
            var singleWorksheetDocument = worksheets.Count == 1;

            foreach (var worksheet in worksheets)
            {
                var tableName = AllocateTableName(usedNames, document.FileName, worksheet.Name, singleWorksheetDocument);
                var columns = CreateTable(_connection, tableName, worksheet.Header ?? [], worksheet.Rows ?? [], cancellationToken);
                RegisterTable(document, tableName, worksheet.Name, columns);
            }

            importStopwatch.Stop();

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Loaded tabular document '{FileName}' into {TableCount} table(s) via artifact loader in {ImportMilliseconds} ms.",
                    document.FileName,
                    worksheets.Count,
                    importStopwatch.ElapsedMilliseconds);
            }
        }
    }

    private bool IsDocumentLoaded(string documentId)
    {
        foreach (var table in _tables.Values)
        {
            if (string.Equals(table.DocumentId, documentId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterTable(
        TabularDocumentRef document,
        string tableName,
        string worksheetName,
        IReadOnlyList<TabularColumnInfo> columns)
    {
        var sourceNames = columns.ToDictionary(c => c.Name, c => c.SourceName, StringComparer.OrdinalIgnoreCase);
        _tables[tableName] = new LoadedTable(tableName, document.DocumentId, worksheetName, document.FileName, sourceNames);

        SaveMetadataEntry(tableName, document.DocumentId, worksheetName, document.FileName, sourceNames);
    }

    private static string AllocateTableName(
        HashSet<string> usedNames,
        string fileName,
        string worksheetName,
        bool singleWorksheet)
    {
        var baseName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(fileName ?? string.Empty), "data");

        // Single-worksheet documents (CSV, TSV, single-sheet workbooks) keep the file base name so
        // existing table names remain stable. Multi-worksheet workbooks qualify each table with its
        // worksheet name so the sheets stay independently addressable.
        var candidate = singleWorksheet || string.IsNullOrWhiteSpace(worksheetName)
            ? baseName
            : SanitizeIdentifier($"{baseName}_{worksheetName}", baseName);

        var unique = candidate;
        var suffix = 2;

        while (!usedNames.Add(unique))
        {
            unique = $"{candidate}_{suffix}";
            suffix++;
        }

        return unique;
    }

    private SqliteConnection OpenConnection()
    {
        string connectionString;

        if (string.IsNullOrEmpty(_databasePath))
        {
            connectionString = "Data Source=:memory:";
        }
        else
        {
            var directory = Path.GetDirectoryName(_databasePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            connectionString = $"Data Source={_databasePath}";
        }

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        EnableDoubleQuotedStringLiterals(connection);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Opened tabular workspace database at '{DatabasePath}'.",
                string.IsNullOrEmpty(_databasePath) ? ":memory:" : _databasePath);
        }

        using var walCommand = connection.CreateCommand();
        walCommand.CommandText = "PRAGMA journal_mode=WAL";
        walCommand.ExecuteNonQuery();

        EnsureMetadataTable(connection);

        // Keep the connection read-only by default. Writes are only enabled inside the narrow windows
        // opened by the table-build and manipulation paths, so a write can never run on a read path.
        SetWritable(connection, false);

        return connection;
    }

    private static void EnableDoubleQuotedStringLiterals(SqliteConnection connection)
    {
        // Microsoft.Data.Sqlite disables SQLite's legacy double-quoted string literal fallback by
        // default, so a value written with double quotes (for example "abc") is parsed as an
        // identifier and fails with "no such column". Language models frequently emit double-quoted
        // string literals, so the tolerant behavior is re-enabled for this sandboxed workspace so
        // model-authored queries succeed instead of erroring on a purely syntactic quoting choice.
        raw.sqlite3_db_config(connection.Handle, SqliteDbConfigDqsDml, 1, out _);
        raw.sqlite3_db_config(connection.Handle, SqliteDbConfigDqsDdl, 1, out _);
    }

    private static void EnsureMetadataTable(SqliteConnection connection)
    {
        if (MetadataTableNeedsRebuild(connection))
        {
            // The metadata schema predates per-worksheet tables. The workspace database is a derived
            // cache of the source documents, so it is safe to drop everything and let the tables be
            // re-imported under the current schema.
            DropAllUserTables(connection);
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS "{MetadataTableName}" (
                "table_name" TEXT PRIMARY KEY,
                "document_id" TEXT NOT NULL,
                "worksheet_name" TEXT,
                "file_name" TEXT NOT NULL,
                "source_names_json" TEXT NOT NULL
            )
            """;
        command.ExecuteNonQuery();
    }

    private static bool MetadataTableNeedsRebuild(SqliteConnection connection)
    {
        using (var existsCommand = connection.CreateCommand())
        {
            existsCommand.CommandText = $"SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{MetadataTableName}'";

            if (existsCommand.ExecuteScalar() is null)
            {
                return false;
            }
        }

        using var infoCommand = connection.CreateCommand();
        infoCommand.CommandText = $"PRAGMA table_info(\"{MetadataTableName}\")";

        using var reader = infoCommand.ExecuteReader();

        while (reader.Read())
        {
            if (string.Equals(reader["name"]?.ToString(), "worksheet_name", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void DropAllUserTables(SqliteConnection connection)
    {
        var tableNames = new List<string>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        foreach (var tableName in tableNames)
        {
            using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP TABLE IF EXISTS {QuoteIdentifier(tableName)}";
            dropCommand.ExecuteNonQuery();
        }
    }

    private void LoadMetadataFromDatabase()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT table_name, document_id, worksheet_name, file_name, source_names_json FROM \"{MetadataTableName}\"";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var tableName = reader.GetString(0);
            var documentId = reader.GetString(1);
            var worksheetName = reader.IsDBNull(2) ? null : reader.GetString(2);
            var fileName = reader.GetString(3);
            var sourceNamesJson = reader.GetString(4);

            var sourceNames = JsonSerializer.Deserialize<Dictionary<string, string>>(sourceNamesJson, _jsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            _tables[tableName] = new LoadedTable(tableName, documentId, worksheetName, fileName, sourceNames);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Loaded {TableCount} tabular workspace metadata entr{Suffix} from the database.", _tables.Count, _tables.Count == 1 ? "y" : "ies");
        }
    }

    private void SaveMetadataEntry(
        string tableName,
        string documentId,
        string worksheetName,
        string fileName,
        IReadOnlyDictionary<string, string> sourceNames)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            INSERT OR REPLACE INTO "{MetadataTableName}" (table_name, document_id, worksheet_name, file_name, source_names_json)
            VALUES ($tableName, $documentId, $worksheetName, $fileName, $sourceNamesJson)
            """;
        command.Parameters.AddWithValue("$tableName", tableName);
        command.Parameters.AddWithValue("$documentId", documentId);
        command.Parameters.AddWithValue("$worksheetName", (object)worksheetName ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileName", fileName);
        command.Parameters.AddWithValue("$sourceNamesJson", JsonSerializer.Serialize(sourceNames, _jsonOptions));
        command.ExecuteNonQuery();
    }

    private IReadOnlyList<TabularColumnInfo> CreateTable(
        SqliteConnection connection,
        string tableName,
        List<string> header,
        List<List<string>> rows,
        CancellationToken cancellationToken)
    {
        if (header.Count == 0)
        {
            // Create an empty placeholder table so the model can still describe and query it.
            using var emptyCommand = connection.CreateCommand();
            emptyCommand.CommandText = $"CREATE TABLE {QuoteIdentifier(tableName)} (\"value\" TEXT)";
            emptyCommand.ExecuteNonQuery();

            return [new TabularColumnInfo("value", "TEXT")];
        }

        // Widen the header so populated cells that have no header still become columns, then flag any
        // embedded subtotal/total rows so aggregate queries can exclude them. This mirrors the streaming
        // Open XML importer so delimited (CSV/TSV) sources are shaped the same way.
        var expandedHeader = TabularWorksheetShaper.ExpandHeader(header, rows);
        var dataColumns = TabularWorkspaceSqliteHelpers.BuildColumns(expandedHeader, rows);
        var hasSubtotalColumn = rows.Any(TabularWorksheetShaper.IsSubtotalRow);
        IReadOnlyList<TabularColumnInfo> columns = hasSubtotalColumn
            ? [.. dataColumns, new TabularColumnInfo(TabularWorksheetShaper.SubtotalColumnName, "INTEGER")]
            : dataColumns;

        TabularWorkspaceSqliteHelpers.CreateTable(connection, tableName, columns);

        if (rows.Count == 0)
        {
            return columns;
        }

        InsertRows(connection, tableName, columns, dataColumns, hasSubtotalColumn, rows, out _, cancellationToken);

        return columns;
    }

    private int InsertRows(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<TabularColumnInfo> columns,
        IReadOnlyList<TabularColumnInfo> dataColumns,
        bool hasSubtotalColumn,
        List<List<string>> rows,
        out int rowsPerBatch,
        CancellationToken cancellationToken)
    {
        rowsPerBatch = 1;
        var stopwatch = Stopwatch.StartNew();
        var columnList = string.Join(", ", columns.Select(column => QuoteIdentifier(column.Name)));
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var parameterNames = new string[columns.Count];

        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var parameterName = $"$p{columnIndex}";
            parameterNames[columnIndex] = parameterName;

            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = DBNull.Value;
            command.Parameters.Add(parameter);
        }

        command.CommandText = $"INSERT INTO {QuoteIdentifier(tableName)} ({columnList}) VALUES ({string.Join(", ", parameterNames)})";
        command.Prepare();

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Starting SQLite import for table '{TableName}' with {ColumnCount} column(s), {RowCount} row(s), and prepared batch size {RowsPerBatch}.",
                tableName,
                columns.Count,
                rows.Count,
                rowsPerBatch);
        }

        var insertCommandCount = 0;

        try
        {
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = rows[rowIndex];

                for (var columnIndex = 0; columnIndex < dataColumns.Count; columnIndex++)
                {
                    var value = columnIndex < row.Count ? row[columnIndex] : null;

                    command.Parameters[columnIndex].Value = value is null || TabularWorkspaceSqliteHelpers.IsNullValue(dataColumns[columnIndex].DeclaredType, value)
                        ? DBNull.Value
                        : TabularWorkspaceSqliteHelpers.NormalizeCellValue(dataColumns[columnIndex].DeclaredType, value);
                }

                if (hasSubtotalColumn)
                {
                    command.Parameters[dataColumns.Count].Value = TabularWorksheetShaper.IsSubtotalRow(row) ? 1L : 0L;
                }

                command.ExecuteNonQuery();
                insertCommandCount++;

                if (_logger.IsEnabled(LogLevel.Debug) &&
                    ((rowIndex + 1) % ImportProgressIntervalRows == 0 || rowIndex == rows.Count - 1))
                {
                    _logger.LogDebug(
                        "SQLite import for table '{TableName}' processed {ProcessedRowCount}/{TotalRowCount} row(s) in {ElapsedMilliseconds} ms.",
                        tableName,
                        rowIndex + 1,
                        rows.Count,
                        stopwatch.ElapsedMilliseconds);
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();

            throw;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Completed SQLite import for table '{TableName}' with {RowCount} row(s), batch size {RowsPerBatch}, and {InsertCommandCount} insert command execution(s) in {ElapsedMilliseconds} ms.",
                tableName,
                rows.Count,
                rowsPerBatch,
                insertCommandCount,
                stopwatch.ElapsedMilliseconds);
        }

        return insertCommandCount;
    }

    private List<TabularTableInfo> BuildTableInfos()
    {
        var infos = new List<TabularTableInfo>(_tables.Count);

        foreach (var (tableName, table) in _tables)
        {
            var columns = new List<TabularColumnInfo>();

            using (var schemaCommand = _connection.CreateCommand())
            {
                schemaCommand.CommandText = $"PRAGMA table_info({QuoteIdentifier(table.TableName)})";

                using var reader = schemaCommand.ExecuteReader();

                while (reader.Read())
                {
                    var name = reader["name"]?.ToString() ?? string.Empty;
                    var type = reader["type"]?.ToString() ?? "TEXT";
                    table.SourceNames.TryGetValue(name, out var sourceName);
                    columns.Add(new TabularColumnInfo(name, type, sourceName));
                }
            }

            long rowCount;

            using (var countCommand = _connection.CreateCommand())
            {
                countCommand.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(table.TableName)}";
                rowCount = Convert.ToInt64(countCommand.ExecuteScalar());
            }

            infos.Add(new TabularTableInfo
            {
                TableName = table.TableName,
                SourceDocumentId = table.DocumentId,
                SourceFileName = table.FileName,
                WorksheetName = table.WorksheetName,
                RowCount = rowCount,
                Columns = columns,
            });
        }

        return infos;
    }

    private static string SanitizeIdentifier(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var c in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        var sanitized = builder.ToString().Trim('_');

        if (string.IsNullOrEmpty(sanitized))
        {
            return fallback;
        }

        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private object TruncateValue(object value)
    {
        if (value is not string text || text.Length <= _options.MaxCellLength)
        {
            return value;
        }

        return string.Concat(text.AsSpan(0, _options.MaxCellLength), "…");
    }

    private static string FormatExportValue(object value)
    {
        return value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    private static async Task WriteCsvRowAsync(
        StreamWriter writer,
        string[] values,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                await writer.WriteAsync(",".AsMemory(), cancellationToken);
            }

            await writer.WriteAsync(EscapeCsvValue(values[i]).AsMemory(), cancellationToken);
        }

        await writer.WriteAsync(Environment.NewLine.AsMemory(), cancellationToken);
    }

    private static string EscapeCsvValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Contains('"', StringComparison.Ordinal) &&
            !value.Contains(',', StringComparison.Ordinal) &&
            !value.Contains('\r', StringComparison.Ordinal) &&
            !value.Contains('\n', StringComparison.Ordinal))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed class LoadedTable
    {
        public LoadedTable(
            string tableName,
            string documentId,
            string worksheetName,
            string fileName,
            IReadOnlyDictionary<string, string> sourceNames)
        {
            TableName = tableName;
            DocumentId = documentId;
            WorksheetName = worksheetName;
            FileName = fileName;
            SourceNames = sourceNames;
        }

        public string TableName { get; }

        public string DocumentId { get; }

        public string WorksheetName { get; }

        public string FileName { get; }

        public IReadOnlyDictionary<string, string> SourceNames { get; }
    }
}
