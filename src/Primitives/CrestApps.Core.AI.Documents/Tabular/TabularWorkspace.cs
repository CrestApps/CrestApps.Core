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
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(contentLoader);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            _connection ??= OpenConnection();

            var desiredTableNames = ComputeTableNames(documents);

            await SynchronizeTablesAsync(documents, desiredTableNames, contentLoader, cancellationToken);

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

            var rows = new List<string[]>();
            var truncated = false;

            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count >= limit)
                {
                    truncated = true;

                    break;
                }

                var row = new string[reader.FieldCount];

                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : Truncate(reader.GetValue(i)?.ToString());
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
        Func<string, CancellationToken, Task<string>> contentLoader,
        CancellationToken cancellationToken)
    {
        foreach (var document in documents)
        {
            if (_tables.ContainsKey(document.DocumentId))
            {
                continue;
            }

            var tableName = desiredTableNames[document.DocumentId];
            var content = await contentLoader(document.DocumentId, cancellationToken);

            CreateTable(_connection, tableName, content);
            _tables[document.DocumentId] = new LoadedTable(tableName, document.FileName);
        }
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        return connection;
    }

    private static void CreateTable(SqliteConnection connection, string tableName, string content)
    {
        var (header, rows) = DelimitedDataParser.Parse(content, null);

        if (header.Count == 0)
        {
            // Create an empty placeholder table so the model can still describe and query it.
            using var emptyCommand = connection.CreateCommand();
            emptyCommand.CommandText = $"CREATE TABLE {QuoteIdentifier(tableName)} (\"value\" TEXT)";
            emptyCommand.ExecuteNonQuery();

            return;
        }

        var columns = BuildColumnNames(header);

        using (var createCommand = connection.CreateCommand())
        {
            var columnDefinitions = string.Join(", ", columns.Select(c => $"{QuoteIdentifier(c)} TEXT"));
            createCommand.CommandText = $"CREATE TABLE {QuoteIdentifier(tableName)} ({columnDefinitions})";
            createCommand.ExecuteNonQuery();
        }

        if (rows.Count == 0)
        {
            return;
        }

        InsertRows(connection, tableName, columns, rows);
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
                    columns.Add(new TabularColumnInfo(name, type));
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

    private static List<string> BuildColumnNames(IReadOnlyList<string> header)
    {
        var columns = new List<string>(header.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Count; i++)
        {
            var name = SanitizeIdentifier(header[i], $"column_{i + 1}");
            var candidate = name;
            var suffix = 2;

            while (!used.Add(candidate))
            {
                candidate = $"{name}_{suffix}";
                suffix++;
            }

            columns.Add(candidate);
        }

        return columns;
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

    private string Truncate(string value)
    {
        if (value is null || value.Length <= _options.MaxCellLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, _options.MaxCellLength), "…");
    }

    private sealed class LoadedTable
    {
        public LoadedTable(string tableName, string fileName)
        {
            TableName = tableName;
            FileName = fileName;
        }

        public string TableName { get; }

        public string FileName { get; }
    }
}
