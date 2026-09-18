using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CrestApps.Core.PostgreSQL.Services;

/// <summary>
/// PostgreSQL implementation of <see cref="IDataSourceContentManager"/>
/// for searching data source embedding indexes using pgvector cosine similarity.
/// </summary>
internal sealed class PostgreSQLDataSourceContentManager : IDataSourceContentManager
{
    private readonly IPostgreSQLClientFactory _clientFactory;
    private readonly ILogger<PostgreSQLDataSourceContentManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLDataSourceContentManager"/> class.
    /// </summary>
    /// <param name="clientFactory">The PostgreSQL client factory.</param>
    /// <param name="logger">The logger.</param>
    public PostgreSQLDataSourceContentManager(
        IPostgreSQLClientFactory clientFactory,
        ILogger<PostgreSQLDataSourceContentManager> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Searches the data source index for the nearest embeddings using pgvector cosine distance.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="embedding">The query embedding vector.</param>
    /// <param name="dataSourceId">The data source ID to filter by.</param>
    /// <param name="topN">The maximum number of results to return.</param>
    /// <param name="filter">An optional OData-style filter expression (pre-translated to SQL WHERE clause).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <remarks>
    /// A database that cannot be reached reads here as an index holding nothing, exactly as it always has. A
    /// caller that has to tell those apart reads <see cref="TrySearchAsync"/> instead.
    /// </remarks>
    public async Task<IEnumerable<DataSourceSearchResult>> SearchAsync(
        IIndexProfileInfo indexProfile,
        float[] embedding,
        string dataSourceId,
        int topN,
        string filter = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TrySearchAsync(indexProfile, embedding, dataSourceId, topN, filter, cancellationToken);

        return outcome.Results;
    }

    /// <summary>
    /// Searches the data source index for the nearest embeddings, reporting a query that could not run as a
    /// failure rather than as an index with nothing in it.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="embedding">The query embedding vector.</param>
    /// <param name="dataSourceId">The data source ID to filter by.</param>
    /// <param name="topN">The maximum number of results to return.</param>
    /// <param name="filter">An optional OData-style filter expression (pre-translated to SQL WHERE clause).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the search.</returns>
    public async Task<DataSourceSearchOutcome> TrySearchAsync(
        IIndexProfileInfo indexProfile,
        float[] embedding,
        string dataSourceId,
        int topN,
        string filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexProfile);
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);

        if (embedding.Length == 0)
        {
            return DataSourceSearchOutcome.Success([]);
        }

        var tableName = PostgreSQLSearchIndexManager.SanitizeTableName(indexProfile.IndexFullName);
        var quotedTableName = PostgreSQLHelpers.QuoteIdentifier(tableName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            var sql = $"""
                SELECT {PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.ReferenceId)},
                       {PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.Title)},
                       {PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.Content)},
                       {PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.ChunkIndex)},
                       {PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.ReferenceType)},
                       1 - ({PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.Embedding)} <=> @embedding) AS score,
                       to_jsonb(t) ->> '{DataSourceConstants.ColumnNames.ContentType}' AS typed_content_type,
                       to_jsonb(t) ->> '{DataSourceConstants.ColumnNames.RootId}' AS typed_root_id,
                       to_jsonb(t) ->> '{DataSourceConstants.ColumnNames.ParentId}' AS typed_parent_id,
                       to_jsonb(t) ->> '{DataSourceConstants.ColumnNames.Page}' AS typed_page
                FROM {quotedTableName} t
                WHERE {PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.DataSourceId)} = @dataSourceId
                """;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                sql += $" AND ({filter})";
            }

            sql += $""" ORDER BY {PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.Embedding)} <=> @embedding LIMIT @topN """;

            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("embedding", new Pgvector.Vector(embedding)));
            command.Parameters.AddWithValue("dataSourceId", dataSourceId);
            command.Parameters.AddWithValue("topN", topN);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var results = new List<DataSourceSearchResult>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var content = reader.IsDBNull(2) ? null : reader.GetString(2);

                if (string.IsNullOrEmpty(content))
                {
                    continue;
                }

                results.Add(new DataSourceSearchResult
                {
                    ReferenceId = reader.IsDBNull(0) ? null : reader.GetString(0),
                    DataSourceId = dataSourceId,
                    Title = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Content = content,
                    ChunkIndex = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    ReferenceType = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Score = reader.IsDBNull(5) ? 0f : reader.GetFloat(5),
                    ContentType = reader.IsDBNull(6) ? KnowledgeObjectTypes.Text : reader.GetString(6),
                    RootId = reader.IsDBNull(7) ? null : reader.GetString(7),
                    ParentId = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Page = reader.IsDBNull(9) || !int.TryParse(reader.GetString(9), out var page) ? null : page,
                });
            }

            return DataSourceSearchOutcome.Success(results);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reported as a failure rather than as no rows: a stopped database and a table with nothing
            // matching in it are the same empty list, and a caller that cannot tell them apart answers a
            // stopped database by stating the content does not exist.
            _logger.LogError(ex, "Error performing data source vector search in PostgreSQL table '{IndexName}'.", tableName);

            return DataSourceSearchOutcome.Failure();
        }
    }

    /// <summary>
    /// Deletes every row belonging to the supplied reference identifiers in one statement.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="referenceIds">The reference identifiers to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the delete ran.</returns>
    public async Task<bool> DeleteByReferenceIdsAsync(
        IIndexProfileInfo indexProfile,
        string dataSourceId,
        IReadOnlyCollection<string> referenceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);
        ArgumentNullException.ThrowIfNull(referenceIds);

        if (referenceIds.Count == 0)
        {
            return true;
        }

        var tableName = PostgreSQLSearchIndexManager.SanitizeTableName(indexProfile.IndexFullName);
        var quotedTableName = PostgreSQLHelpers.QuoteIdentifier(tableName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = $"""
                DELETE FROM {quotedTableName}
                WHERE {PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.DataSourceId)} = @dataSourceId
                    AND {PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.ReferenceId)} = ANY(@referenceIds)
                """;
            command.Parameters.AddWithValue("dataSourceId", dataSourceId);
            command.Parameters.AddWithValue("referenceIds", referenceIds.ToArray());

            await command.ExecuteNonQueryAsync(cancellationToken);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Returning false lets the caller fall back to deleting by identifier list, which is slower but
            // always available.
            _logger.LogWarning(ex, "Failed to delete by reference id in PostgreSQL table '{TableName}'.", tableName);

            return false;
        }
    }

    /// <summary>
    /// Deletes all documents from the index table that match the specified data source ID.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="dataSourceId">The data source ID whose documents should be deleted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<long> DeleteByDataSourceIdAsync(
        IIndexProfileInfo indexProfile,
        string dataSourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);

        var tableName = PostgreSQLSearchIndexManager.SanitizeTableName(indexProfile.IndexFullName);
        var quotedTableName = PostgreSQLHelpers.QuoteIdentifier(tableName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = $"""DELETE FROM {quotedTableName} WHERE {PostgreSQLHelpers.SanitizeColumnName(DataSourceConstants.ColumnNames.DataSourceId)} = @dataSourceId""";
            command.Parameters.AddWithValue("dataSourceId", dataSourceId);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

            return rowsAffected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting documents by data source ID '{DataSourceId}' from PostgreSQL table '{IndexName}'.", dataSourceId, tableName);

            return 0;
        }
    }
}
