using System.Collections.Concurrent;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Default <see cref="ITabularWorkspaceManager"/> implementation backed by in-memory SQLite
/// databases. The live database for a conversation is <b>request-scoped</b>: it is built lazily on
/// first use within a request, reused for every tool call in that request, and disposed when the
/// request completes (reference-counted so concurrent requests for the same conversation are safe).
/// A lightweight, replayable manipulation journal is retained per conversation so changes are
/// reapplied when the database is rebuilt in a later request. A background sweep evicts stale
/// journals and acts as a backstop for any request that fails to release.
/// </summary>
public sealed class TabularWorkspaceManager : ITabularWorkspaceManager, IDisposable
{
    private readonly ConcurrentDictionary<string, WorkspaceState> _workspaces = new(StringComparer.Ordinal);
    private readonly TabularWorkspaceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TabularWorkspaceManager> _logger;
    private readonly ITimer _evictionTimer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabularWorkspaceManager"/> class.
    /// </summary>
    /// <param name="options">The workspace options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public TabularWorkspaceManager(
        IOptions<TabularWorkspaceOptions> options,
        TimeProvider timeProvider,
        ILogger<TabularWorkspaceManager> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        var sweepInterval = _options.SweepInterval > TimeSpan.Zero ? _options.SweepInterval : TimeSpan.FromMinutes(2);
        _evictionTimer = _timeProvider.CreateTimer(_ => Sweep(), null, sweepInterval, sweepInterval);
    }

    /// <summary>
    /// Ensures the request's in-memory database is built and synchronized with the supplied documents.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    /// <param name="requestId">The current request/prompt identifier.</param>
    /// <param name="documents">The tabular documents that should be available.</param>
    /// <param name="contentLoader">The document content loader.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IReadOnlyList<TabularTableInfo>> EnsureReadyAsync(
        string conversationKey,
        string requestId,
        IReadOnlyList<TabularDocumentRef> documents,
        Func<string, CancellationToken, Task<string>> contentLoader,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationKey);
        ArgumentException.ThrowIfNullOrEmpty(requestId);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(contentLoader);

        var state = _workspaces.GetOrAdd(conversationKey, _ => new WorkspaceState());

        await state.Gate.WaitAsync(cancellationToken);

        try
        {
            var desiredTableNames = ComputeTableNames(documents);
            var rebuilding = state.Connection is null;

            if (rebuilding)
            {
                state.Connection = OpenConnection();
                state.Tables.Clear();
            }

            await SynchronizeTablesAsync(state, documents, desiredTableNames, contentLoader, cancellationToken);

            if (rebuilding)
            {
                ReplayJournal(state);
            }

            // Mark this request as an active user of the workspace so the live database survives
            // until every concurrent request for the conversation has released it.
            state.ActiveRequestIds.Add(requestId);
            state.LastAccessedUtc = _timeProvider.GetUtcNow();

            return BuildTableInfos(state);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>
    /// Gets the tables currently loaded for the conversation.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IReadOnlyList<TabularTableInfo>> GetTablesAsync(string conversationKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationKey);

        if (!_workspaces.TryGetValue(conversationKey, out var state))
        {
            return [];
        }

        await state.Gate.WaitAsync(cancellationToken);

        try
        {
            if (state.Connection is null)
            {
                return [];
            }

            state.LastAccessedUtc = _timeProvider.GetUtcNow();

            return BuildTableInfos(state);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>
    /// Runs a read-only query against the conversation's live in-memory database.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    /// <param name="sql">The read-only SQL query.</param>
    /// <param name="maxRows">The maximum number of rows to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<TabularQueryResult> QueryAsync(string conversationKey, string sql, int maxRows, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationKey);

        var statement = TabularSqlGuard.EnsureReadOnlyQuery(sql);
        var state = GetReadyState(conversationKey);

        var limit = maxRows <= 0 || maxRows > _options.MaxRowsPerQuery
            ? _options.MaxRowsPerQuery
            : maxRows;

        await state.Gate.WaitAsync(cancellationToken);

        try
        {
            if (state.Connection is null)
            {
                throw new TabularSqlException("The tabular workspace is not loaded. Load the data before querying it.");
            }

            using var command = state.Connection.CreateCommand();
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

            state.LastAccessedUtc = _timeProvider.GetUtcNow();

            return new TabularQueryResult
            {
                Columns = columns,
                Rows = rows,
                Truncated = truncated,
            };
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>
    /// Runs a manipulation or schema statement against the conversation's live in-memory database
    /// and records it in the rebuild journal.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    /// <param name="sql">The manipulation or schema statement.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<TabularCommandResult> ExecuteAsync(string conversationKey, string sql, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationKey);

        var statement = TabularSqlGuard.EnsureCommand(sql);
        var state = GetReadyState(conversationKey);

        await state.Gate.WaitAsync(cancellationToken);

        try
        {
            if (state.Connection is null)
            {
                throw new TabularSqlException("The tabular workspace is not loaded. Load the data before modifying it.");
            }

            using var command = state.Connection.CreateCommand();
            command.CommandText = statement;
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            var affected = await command.ExecuteNonQueryAsync(cancellationToken);

            // Record the manipulation so it can be replayed when the workspace is rebuilt
            // in a later request after the live database has been disposed.
            state.MutationJournal.Add(statement);
            state.LastAccessedUtc = _timeProvider.GetUtcNow();

            return new TabularCommandResult(affected);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>
    /// Releases the current request's hold on the conversation workspace, disposing the live
    /// in-memory database when no other request is still using it.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    /// <param name="requestId">The completing request/prompt identifier.</param>
    public void ReleaseRequest(string conversationKey, string requestId)
    {
        if (string.IsNullOrEmpty(conversationKey) || string.IsNullOrEmpty(requestId))
        {
            return;
        }

        if (!_workspaces.TryGetValue(conversationKey, out var state))
        {
            return;
        }

        // Bound the wait so a stuck concurrent operation cannot block request teardown; the idle
        // sweep reclaims the database as a backstop if the gate cannot be acquired here.
        if (!state.Gate.Wait(TimeSpan.FromSeconds(Math.Max(1, _options.CommandTimeoutSeconds))))
        {
            return;
        }

        try
        {
            state.ActiveRequestIds.Remove(requestId);

            if (state.ActiveRequestIds.Count == 0 && state.Connection is not null)
            {
                DisposeConnection(state, conversationKey, "request completed");
            }

            state.LastAccessedUtc = _timeProvider.GetUtcNow();
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>
    /// Removes a conversation workspace and disposes its in-memory database and journal.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    public void RemoveConversation(string conversationKey)
    {
        if (string.IsNullOrEmpty(conversationKey))
        {
            return;
        }

        if (_workspaces.TryRemove(conversationKey, out var state))
        {
            state.Dispose();
        }
    }

    /// <summary>
    /// Disposes the eviction timer and all in-memory workspaces.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _evictionTimer.Dispose();

        foreach (var state in _workspaces.Values)
        {
            state.Dispose();
        }

        _workspaces.Clear();
    }

    private WorkspaceState GetReadyState(string conversationKey)
    {
        if (!_workspaces.TryGetValue(conversationKey, out var state) || state.Connection is null)
        {
            throw new TabularSqlException("The tabular workspace is not loaded. Load the data before querying it.");
        }

        return state;
    }

    private void DisposeConnection(WorkspaceState state, string conversationKey, string reason)
    {
        var tableCount = state.Tables.Count;

        state.Connection?.Dispose();
        state.Connection = null;
        state.Tables.Clear();

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Disposed in-memory tabular database for conversation '{ConversationKey}' ({Reason}). It rebuilds from {DocumentCount} document(s) and {MutationCount} recorded manipulation(s) on next use.",
                conversationKey, reason, tableCount, state.MutationJournal.Count);
        }
    }

    private static async Task SynchronizeTablesAsync(
        WorkspaceState state,
        IReadOnlyList<TabularDocumentRef> documents,
        Dictionary<string, string> desiredTableNames,
        Func<string, CancellationToken, Task<string>> contentLoader,
        CancellationToken cancellationToken)
    {
        var desiredDocumentIds = new HashSet<string>(documents.Select(d => d.DocumentId), StringComparer.Ordinal);

        // Drop tables for documents that are no longer present.
        foreach (var loaded in state.Tables.Keys.Where(id => !desiredDocumentIds.Contains(id)).ToList())
        {
            if (state.Tables.TryGetValue(loaded, out var table))
            {
                DropTable(state.Connection, table.TableName);
            }

            state.Tables.Remove(loaded);
        }

        foreach (var document in documents)
        {
            if (state.Tables.ContainsKey(document.DocumentId))
            {
                continue;
            }

            var tableName = desiredTableNames[document.DocumentId];
            var content = await contentLoader(document.DocumentId, cancellationToken);

            CreateTable(state.Connection, tableName, content);
            state.Tables[document.DocumentId] = new LoadedTable(tableName, document.FileName);
        }
    }

    private void ReplayJournal(WorkspaceState state)
    {
        if (state.MutationJournal.Count == 0)
        {
            return;
        }

        foreach (var statement in state.MutationJournal)
        {
            try
            {
                using var command = state.Connection.CreateCommand();
                command.CommandText = statement;
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                // A manipulation may no longer apply (for example, it targeted a removed table).
                // Replay is best-effort so a single stale statement does not break the rebuild.
                _logger.LogWarning(ex, "Skipping a tabular manipulation that could not be replayed during workspace rebuild.");
            }
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

    private static void DropTable(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS {QuoteIdentifier(tableName)}";
        command.ExecuteNonQuery();
    }

    private static List<TabularTableInfo> BuildTableInfos(WorkspaceState state)
    {
        var infos = new List<TabularTableInfo>(state.Tables.Count);

        foreach (var (documentId, table) in state.Tables)
        {
            var columns = new List<TabularColumnInfo>();

            using (var schemaCommand = state.Connection.CreateCommand())
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

            using (var countCommand = state.Connection.CreateCommand())
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
            // deterministically using the document id so rebuilds stay stable.
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

    private void Sweep()
    {
        if (_disposed)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();

        foreach (var (key, state) in _workspaces)
        {
            if (!state.Gate.Wait(0))
            {
                continue;
            }

            try
            {
                var idle = now - state.LastAccessedUtc;

                if (idle > _options.JournalRetention)
                {
                    if (_workspaces.TryRemove(key, out var removed))
                    {
                        removed.Connection?.Dispose();
                        removed.Connection = null;
                    }

                    continue;
                }

                // Backstop: dispose the heavy in-memory database if a request leaked without
                // releasing it. The journal is retained so the data rebuilds on next use.
                if (state.Connection is not null && idle > _options.IdleTimeout)
                {
                    state.ActiveRequestIds.Clear();
                    DisposeConnection(state, key, "idle backstop");
                }
            }
            finally
            {
                state.Gate.Release();
            }
        }
    }

    private sealed class WorkspaceState : IDisposable
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public SqliteConnection Connection { get; set; }

        public Dictionary<string, LoadedTable> Tables { get; } = new(StringComparer.Ordinal);

        public List<string> MutationJournal { get; } = [];

        public HashSet<string> ActiveRequestIds { get; } = new(StringComparer.Ordinal);

        public DateTimeOffset LastAccessedUtc { get; set; }

        public void Dispose()
        {
            Connection?.Dispose();
            Connection = null;
            Gate.Dispose();
        }
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
