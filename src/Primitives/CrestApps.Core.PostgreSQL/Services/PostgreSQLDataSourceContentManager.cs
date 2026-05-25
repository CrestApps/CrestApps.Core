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
    public async Task<IEnumerable<DataSourceSearchResult>> SearchAsync(
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
            return [];
        }

        var tableName = PostgreSQLSearchIndexManager.SanitizeTableName(indexProfile.IndexFullName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            var sql = $"""
            SELECT "{DataSourceConstants.ColumnNames.ReferenceId}",
                   "{DataSourceConstants.ColumnNames.Title}",
                   "{DataSourceConstants.ColumnNames.Content}",
                   "{DataSourceConstants.ColumnNames.ChunkIndex}",
                   "{DataSourceConstants.ColumnNames.ReferenceType}",
                   1 - ("{DataSourceConstants.ColumnNames.Embedding}" <=> @embedding) AS score
            FROM "{tableName}"
            WHERE "{DataSourceConstants.ColumnNames.DataSourceId}" = @dataSourceId
            """;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                sql += $" AND ({filter})";
            }

            sql += $""" ORDER BY "{DataSourceConstants.ColumnNames.Embedding}" <=> @embedding LIMIT @topN """;

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
                    Title = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Content = content,
                    ChunkIndex = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    ReferenceType = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Score = reader.IsDBNull(5) ? 0f : reader.GetFloat(5),
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing data source vector search in PostgreSQL table '{IndexName}'.", tableName);

            return [];
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

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = $"DELETE FROM \"{tableName}\" WHERE \"{DataSourceConstants.ColumnNames.DataSourceId}\" = @dataSourceId";
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
