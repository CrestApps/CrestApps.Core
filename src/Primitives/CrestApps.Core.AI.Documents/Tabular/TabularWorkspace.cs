using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// A request-scoped, in-memory SQLite database that holds the tabular files for a single AI prompt.
/// The workspace is built lazily on the first tabular tool call, reused across every tabular tool
/// call within that same prompt, and disposed when the invocation scope ends. Each prompt rebuilds a
/// fresh copy from the uploaded files, so any in-memory manipulation is naturally discarded when the
/// prompt completes — the originally uploaded file is never modified.
/// </summary>
internal sealed class TabularWorkspace : IDisposable
{
    private readonly TabularWorkspaceOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, LoadedTable> _tables = new(StringComparer.Ordinal);
    private SqliteConnection _connection;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabularWorkspace"/> class.
    /// </summary>
    /// <param name="options">The workspace options.</param>
    public TabularWorkspace(TabularWorkspaceOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Ensures the in-memory database is built and contains a table for each supplied document.
    /// Loading is lazy: <paramref name="contentLoader"/> is only invoked for documents that do not
    /// yet have a table. Calling this multiple times within the same prompt reuses the already-built
    /// tables rather than recreating them.
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
    /// Ensures the in-memory database is built and contains a table for each supplied document.
    /// Loading is lazy: <paramref name="artifactLoader"/> is only invoked for documents that do not
    /// yet have a table. Calling this multiple times within the same workspace reuses the
    /// already-built tables rather than recreating them.
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
    /// Runs a single data-manipulation or schema statement against the in-memory database. The change
    /// applies only to the in-memory copy and is discarded when the prompt completes.
    /// </summary>
    /// <param name="sql">The manipulation or schema statement.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The command result.</returns>
    public async Task<TabularCommandResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        var statement = TabularSqlGuard.EnsureCommand(sql);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            EnsureLoaded();

            using var command = _connection.CreateCommand();
            command.CommandText = statement;
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            var affected = await command.ExecuteNonQueryAsync(cancellationToken);

            return new TabularCommandResult(affected);
        }
        finally
        {
            _gate.Release();
        }
    }

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

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columns = new List<string>(reader.FieldCount);

            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
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
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Disposes the in-memory database and releases the concurrency gate.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection?.Dispose();
        _connection = null;
        _gate.Dispose();
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
        }
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        return connection;
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
