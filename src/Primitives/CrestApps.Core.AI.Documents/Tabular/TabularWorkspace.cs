using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

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

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TabularWorkspaceOptions _options;
    private readonly string _databasePath;
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
        string databasePath = null)
    {
        _options = options;
        _databasePath = databasePath;
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(artifactLoader);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            _connection ??= OpenConnection();

            if (_tables.Count == 0)
            {
                LoadMetadataFromDatabase();
            }

            var desiredTableNames = ComputeTableNames(documents);

            await SynchronizeTablesAsync(documents, desiredTableNames, artifactLoader, cancellationToken);

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

    private async Task SynchronizeTablesAsync(
        IReadOnlyList<TabularDocumentRef> documents,
        Dictionary<string, string> desiredTableNames,
        Func<TabularDocumentRef, CancellationToken, Task<TabularDocumentArtifact>> artifactLoader,
        CancellationToken cancellationToken)
    {
        foreach (var document in documents)
        {
            if (_tables.ContainsKey(document.DocumentId))
            {
                continue;
            }

            var tableName = desiredTableNames[document.DocumentId];
            var artifact = await artifactLoader(document, cancellationToken);

            var columns = CreateTable(_connection, tableName, artifact);
            var sourceNames = columns.ToDictionary(c => c.Name, c => c.SourceName, StringComparer.OrdinalIgnoreCase);
            _tables[document.DocumentId] = new LoadedTable(tableName, document.FileName, sourceNames);

            SaveMetadataEntry(document.DocumentId, tableName, document.FileName, sourceNames);
        }
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

        using var walCommand = connection.CreateCommand();
        walCommand.CommandText = "PRAGMA journal_mode=WAL";
        walCommand.ExecuteNonQuery();

        EnsureMetadataTable(connection);

        return connection;
    }

    private static void EnsureMetadataTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS "{MetadataTableName}" (
                "document_id" TEXT PRIMARY KEY,
                "table_name" TEXT NOT NULL,
                "file_name" TEXT NOT NULL,
                "source_names_json" TEXT NOT NULL
            )
            """;
        command.ExecuteNonQuery();
    }

    private void LoadMetadataFromDatabase()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT document_id, table_name, file_name, source_names_json FROM \"{MetadataTableName}\"";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var documentId = reader.GetString(0);
            var tableName = reader.GetString(1);
            var fileName = reader.GetString(2);
            var sourceNamesJson = reader.GetString(3);

            var sourceNames = JsonSerializer.Deserialize<Dictionary<string, string>>(sourceNamesJson, _jsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            _tables[documentId] = new LoadedTable(tableName, fileName, sourceNames);
        }
    }

    private void SaveMetadataEntry(string documentId, string tableName, string fileName, IReadOnlyDictionary<string, string> sourceNames)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            INSERT OR REPLACE INTO "{MetadataTableName}" (document_id, table_name, file_name, source_names_json)
            VALUES ($documentId, $tableName, $fileName, $sourceNamesJson)
            """;
        command.Parameters.AddWithValue("$documentId", documentId);
        command.Parameters.AddWithValue("$tableName", tableName);
        command.Parameters.AddWithValue("$fileName", fileName);
        command.Parameters.AddWithValue("$sourceNamesJson", JsonSerializer.Serialize(sourceNames, _jsonOptions));
        command.ExecuteNonQuery();
    }

    private static List<TabularColumnInfo> CreateTable(SqliteConnection connection, string tableName, TabularDocumentArtifact artifact)
    {
        artifact ??= new TabularDocumentArtifact();
        var header = artifact.Header ?? [];
        var rows = artifact.Rows ?? [];

        if (header.Count == 0)
        {
            // Create an empty placeholder table so the model can still describe and query it.
            using var emptyCommand = connection.CreateCommand();
            emptyCommand.CommandText = $"CREATE TABLE {QuoteIdentifier(tableName)} (\"value\" TEXT)";
            emptyCommand.ExecuteNonQuery();

            return [new TabularColumnInfo("value", "TEXT")];
        }

        var columns = BuildColumns(header);
        var columnNames = columns.Select(c => c.Name).ToList();

        using (var createCommand = connection.CreateCommand())
        {
            var columnDefinitions = string.Join(", ", columnNames.Select(c => $"{QuoteIdentifier(c)} TEXT"));
            createCommand.CommandText = $"CREATE TABLE {QuoteIdentifier(tableName)} ({columnDefinitions})";
            createCommand.ExecuteNonQuery();
        }

        if (rows.Count == 0)
        {
            return columns;
        }

        InsertRows(connection, tableName, columnNames, rows);

        return columns;
    }

    private static void InsertRows(SqliteConnection connection, string tableName, List<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var columnList = string.Join(", ", columns.Select(QuoteIdentifier));
        var parameterList = string.Join(", ", columns.Select((_, i) => $"$p{i}"));
        command.CommandText = $"INSERT INTO {QuoteIdentifier(tableName)} ({columnList}) VALUES ({parameterList})";

        var parameters = new SqliteParameter[columns.Count];

        for (var i = 0; i < columns.Count; i++)
        {
            parameters[i] = command.CreateParameter();
            parameters[i].ParameterName = $"$p{i}";
            command.Parameters.Add(parameters[i]);
        }

        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                parameters[i].Value = i < row.Count ? (object)(row[i] ?? string.Empty) : DBNull.Value;
            }

            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private List<TabularTableInfo> BuildTableInfos()
    {
        var infos = new List<TabularTableInfo>(_tables.Count);

        foreach (var (documentId, table) in _tables)
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
                SourceDocumentId = documentId,
                SourceFileName = table.FileName,
                RowCount = rowCount,
                Columns = columns,
            });
        }

        return infos;
    }

    private static Dictionary<string, string> ComputeTableNames(IReadOnlyList<TabularDocumentRef> documents)
    {
        var baseNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var baseNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            var baseName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(document.FileName ?? string.Empty), "data");
            baseNames[document.DocumentId] = baseName;
            baseNameCounts[baseName] = baseNameCounts.TryGetValue(baseName, out var count) ? count + 1 : 1;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            var baseName = baseNames[document.DocumentId];

            // When multiple documents resolve to the same base name, disambiguate
            // deterministically using the document id so table names stay stable.
            var tableName = baseNameCounts[baseName] > 1
                ? $"{baseName}_{SanitizeIdentifier(document.DocumentId[..Math.Min(8, document.DocumentId.Length)], "doc")}"
                : baseName;

            result[document.DocumentId] = tableName;
        }

        return result;
    }

    private static List<TabularColumnInfo> BuildColumns(List<string> header)
    {
        var columns = new List<TabularColumnInfo>(header.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Count; i++)
        {
            var sourceName = header[i];
            var name = SanitizeIdentifier(GetPreferredHeaderName(sourceName), $"column_{i + 1}");
            var candidate = name;
            var suffix = 2;

            while (!used.Add(candidate))
            {
                candidate = $"{name}_{suffix}";
                suffix++;
            }

            columns.Add(new TabularColumnInfo(candidate, "TEXT", sourceName));
        }

        return columns;
    }

    private static string GetPreferredHeaderName(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return header;
        }

        var trimmed = header.Trim();
        var slashIndex = trimmed.IndexOf('/');

        if (slashIndex > 0)
        {
            var prefix = trimmed[..slashIndex].Trim();

            if (IsCompactHeaderCode(prefix))
            {
                return prefix;
            }
        }

        return trimmed;
    }

    private static bool IsCompactHeaderCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return false;
        }

        return value.All(c => char.IsLetterOrDigit(c) || c == '_');
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
            string fileName,
            IReadOnlyDictionary<string, string> sourceNames)
        {
            TableName = tableName;
            FileName = fileName;
            SourceNames = sourceNames;
        }

        public string TableName { get; }

        public string FileName { get; }

        public IReadOnlyDictionary<string, string> SourceNames { get; }
    }
}
