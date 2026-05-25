using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Support;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace CrestApps.Core.PostgreSQL.Services;

/// <summary>
/// PostgreSQL implementation of <see cref="ISearchDocumentManager"/>
/// for adding, updating, and deleting documents in search index tables.
/// </summary>
internal sealed class PostgreSQLSearchDocumentManager : ISearchDocumentManager
{
    private readonly IPostgreSQLClientFactory _clientFactory;
    private readonly IEnumerable<ISearchDocumentHandler> _handlers;
    private readonly ILogger<PostgreSQLSearchDocumentManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLSearchDocumentManager"/> class.
    /// </summary>
    /// <param name="clientFactory">The PostgreSQL client factory.</param>
    /// <param name="handlers">The search document handlers.</param>
    /// <param name="logger">The logger.</param>
    public PostgreSQLSearchDocumentManager(
        IPostgreSQLClientFactory clientFactory,
        IEnumerable<ISearchDocumentHandler> handlers,
        ILogger<PostgreSQLSearchDocumentManager> logger)
    {
        _clientFactory = clientFactory;
        _handlers = handlers;
        _logger = logger;
    }

    /// <summary>
    /// Adds or updates documents in the index table using upsert (INSERT ... ON CONFLICT).
    /// </summary>
    /// <param name="profile">The index profile.</param>
    /// <param name="documents">The documents to index.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<bool> AddOrUpdateAsync(IIndexProfileInfo profile, IReadOnlyCollection<IndexDocument> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
        {
            return true;
        }

        var tableName = PostgreSQLSearchIndexManager.SanitizeTableName(profile.IndexFullName);

        try
        {
            var dataSource = _clientFactory.Create();

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            await EnsureDataSourceFilterColumnAsync(connection, tableName, documents, cancellationToken);

            var keyColumnName = await GetPrimaryKeyColumnAsync(connection, tableName, cancellationToken);

            foreach (var document in documents)
            {
                var columns = new List<string>();
                var paramNames = new List<string>();
                var updateClauses = new List<string>();
                var parameters = new List<NpgsqlParameter>();
                var paramIndex = 0;

                foreach (var field in document.Fields)
                {
                    var columnName = field.Key;
                    var paramName = $"@p{paramIndex}";
                    columns.Add($"\"{columnName}\"");
                    paramNames.Add(paramName);

                    if (!string.Equals(columnName, keyColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        updateClauses.Add($"\"{columnName}\" = {paramName}");
                    }

                    var parameter = CreateParameter(paramName, columnName, field.Value);
                    parameters.Add(parameter);
                    paramIndex++;
                }

                var conflictColumn = !string.IsNullOrEmpty(keyColumnName) ? keyColumnName : columns[0].Trim('"');

                var sql = $"INSERT INTO \"{tableName}\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", paramNames)}) " +
                          $"ON CONFLICT (\"{conflictColumn}\") DO UPDATE SET {string.Join(", ", updateClauses)}";

                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddRange(parameters.ToArray());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await NotifyDocumentsAddedOrUpdatedAsync(profile, documents, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing documents in PostgreSQL table '{IndexName}'.", tableName.SanitizeForLog());

            return false;
        }
    }

    /// <summary>
    /// Deletes specific documents by their IDs from the index table.
    /// </summary>
    /// <param name="profile">The index profile.</param>
    /// <param name="documentIds">The document IDs to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task DeleteAsync(IIndexProfileInfo profile, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(documentIds);

        var ids = documentIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var tableName = PostgreSQLSearchIndexManager.SanitizeTableName(profile.IndexFullName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            var keyColumnName = await GetPrimaryKeyColumnAsync(connection, tableName, cancellationToken);
            if (string.IsNullOrEmpty(keyColumnName))
            {
                _logger.LogWarning("Unable to determine primary key for table '{TableName}'. Cannot delete documents.", tableName.SanitizeForLog());

                return;
            }

            await using var command = connection.CreateCommand();

            var paramNames = new List<string>();
            for (var i = 0; i < ids.Count; i++)
            {
                var paramName = $"@id{i}";
                paramNames.Add(paramName);
                command.Parameters.AddWithValue(paramName, ids[i]);
            }

            command.CommandText = $"DELETE FROM \"{tableName}\" WHERE \"{keyColumnName}\" IN ({string.Join(", ", paramNames)})";
            await command.ExecuteNonQueryAsync(cancellationToken);

            await NotifyDocumentsDeletedAsync(profile, ids, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting documents from PostgreSQL table '{IndexName}'.", tableName.SanitizeForLog());
        }
    }

    /// <summary>
    /// Deletes all documents from the index table.
    /// </summary>
    /// <param name="profile">The index profile.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task DeleteAllAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var tableName = PostgreSQLSearchIndexManager.SanitizeTableName(profile.IndexFullName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = $"TRUNCATE TABLE \"{tableName}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting all documents from PostgreSQL table '{IndexName}'.", tableName.SanitizeForLog());
        }
    }

    /// <summary>
    /// Creates a PostgreSQL parameter for a document field.
    /// </summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="columnName">The target column name.</param>
    /// <param name="value">The field value.</param>
    private static NpgsqlParameter CreateParameter(string parameterName, string columnName, object value)
    {
        if (value is float[] vectorArray)
        {
            return new NpgsqlParameter(parameterName, new Pgvector.Vector(vectorArray));
        }

        if (IsJsonField(columnName, value))
        {
            return new NpgsqlParameter(parameterName, NpgsqlDbType.Jsonb)
            {
                Value = SerializeJsonValue(value),
            };
        }

        return new NpgsqlParameter(parameterName, ResolveValue(value));
    }

    /// <summary>
    /// Resolves a scalar PostgreSQL value.
    /// </summary>
    /// <param name="value">The field value.</param>
    private static object ResolveValue(object value)
    {
        return value ?? DBNull.Value;
    }

    /// <summary>
    /// Determines whether the field should be stored as JSON.
    /// </summary>
    /// <param name="columnName">The target column name.</param>
    /// <param name="value">The field value.</param>
    private static bool IsJsonField(string columnName, object value)
    {
        return string.Equals(columnName, DataSourceConstants.ColumnNames.Filters, StringComparison.OrdinalIgnoreCase) ||
            value is JsonNode;
    }

    /// <summary>
    /// Serializes a field value to JSON.
    /// </summary>
    /// <param name="value">The field value.</param>
    private static string SerializeJsonValue(object value)
    {
        if (value is null)
        {
            return "{}";
        }

        if (value is JsonNode jsonNode)
        {
            return jsonNode.ToJsonString();
        }

        return JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// Ensures the filters column exists for data-source documents.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="documents">The documents being indexed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task EnsureDataSourceFilterColumnAsync(NpgsqlConnection connection, string tableName, IReadOnlyCollection<IndexDocument> documents, CancellationToken cancellationToken)
    {
        if (!documents.Any(document => document.Fields.ContainsKey(DataSourceConstants.ColumnNames.Filters)))
        {
            return;
        }

        if (await ColumnExistsAsync(connection, tableName, DataSourceConstants.ColumnNames.Filters, cancellationToken))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE \"{tableName}\" ADD COLUMN IF NOT EXISTS \"{DataSourceConstants.ColumnNames.Filters}\" JSONB";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Determines whether a table column exists.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task<bool> ColumnExistsAsync(NpgsqlConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_name = @tableName
                    AND column_name = @columnName)
            """;
        command.Parameters.AddWithValue("tableName", tableName);
        command.Parameters.AddWithValue("columnName", columnName);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is true;
    }

    private static async Task<string> GetPrimaryKeyColumnAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.attname
            FROM pg_index i
            JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE i.indrelid = @tableName::regclass AND i.indisprimary
            LIMIT 1
            """;
        command.Parameters.AddWithValue("tableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result as string;
    }

    private async Task NotifyDocumentsAddedOrUpdatedAsync(IIndexProfileInfo profile, IReadOnlyCollection<IndexDocument> documents, CancellationToken cancellationToken)
    {
        var handlers = _handlers.ToArray();
        if (handlers.Length == 0)
        {
            return;
        }

        var documentIds = documents
            .Select(document => document.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        if (documentIds.Length == 0)
        {
            return;
        }

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Notifying {HandlerCount} search document handler(s) after add/update for index '{IndexName}' with {DocumentCount} document id(s).", handlers.Length, profile.IndexFullName.SanitizeForLog(), documentIds.Length);
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler.DocumentsAddedOrUpdatedAsync(profile, documentIds, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search document handler '{HandlerType}' failed after indexing documents into '{IndexName}'.", handler.GetType().Name, profile.IndexFullName.SanitizeForLog());
            }
        }
    }

    private async Task NotifyDocumentsDeletedAsync(IIndexProfileInfo profile, List<string> documentIds, CancellationToken cancellationToken)
    {
        var handlers = _handlers.ToArray();
        if (handlers.Length == 0 || documentIds.Count == 0)
        {
            return;
        }

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Notifying {HandlerCount} search document handler(s) after delete for index '{IndexName}' with {DocumentCount} document id(s).", handlers.Length, profile.IndexFullName.SanitizeForLog(), documentIds.Count);
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler.DocumentsDeletedAsync(profile, documentIds, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search document handler '{HandlerType}' failed after deleting documents from '{IndexName}'.", handler.GetType().Name, profile.IndexFullName.SanitizeForLog());
            }
        }
    }
}
