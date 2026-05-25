using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.PostgreSQL;
using CrestApps.Core.PostgreSQL.Services;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CrestApps.Core.AI.PostgreSQL.Services;

/// <summary>
/// PostgreSQL implementation of <see cref="IMemoryVectorSearchService"/>
/// for searching AI memory records using pgvector cosine distance.
/// </summary>
internal sealed class PostgreSQLMemoryVectorSearchService : IMemoryVectorSearchService
{
    private readonly IPostgreSQLClientFactory _clientFactory;
    private readonly ILogger<PostgreSQLMemoryVectorSearchService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLMemoryVectorSearchService"/> class.
    /// </summary>
    /// <param name="clientFactory">The PostgreSQL client factory.</param>
    /// <param name="logger">The logger.</param>
    public PostgreSQLMemoryVectorSearchService(
        IPostgreSQLClientFactory clientFactory,
        ILogger<PostgreSQLMemoryVectorSearchService> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Searches the specified index for the nearest memory entries to the given embedding vector,
    /// filtered by user ID.
    /// </summary>
    /// <param name="indexProfile">The search index profile that identifies the backing vector store.</param>
    /// <param name="embedding">The query embedding vector to compare against stored entries.</param>
    /// <param name="userId">The identifier of the user whose memories are searched.</param>
    /// <param name="topN">The maximum number of results to return.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<IEnumerable<AIMemorySearchResult>> SearchAsync(
        SearchIndexProfile indexProfile,
        float[] embedding,
        string userId,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (embedding is null || embedding.Length == 0 || string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        var tableName = PostgreSQLHelpers.SanitizeTableName(indexProfile.IndexFullName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = $"SELECT \"{MemoryConstants.ColumnNames.MemoryId}\", " +
                                 $"\"{MemoryConstants.ColumnNames.Name}\", " +
                                 $"\"{MemoryConstants.ColumnNames.Description}\", " +
                                 $"\"{MemoryConstants.ColumnNames.Content}\", " +
                                 $"\"{MemoryConstants.ColumnNames.UpdatedUtc}\", " +
                                 $"1 - (\"{MemoryConstants.ColumnNames.Embedding}\" <=> @embedding) AS score " +
                                 $"FROM \"{tableName}\" " +
                                 $"WHERE \"{MemoryConstants.ColumnNames.UserId}\" = @userId " +
                                 $"ORDER BY \"{MemoryConstants.ColumnNames.Embedding}\" <=> @embedding " +
                                 $"LIMIT @topN";

            command.Parameters.Add(new NpgsqlParameter("embedding", new Pgvector.Vector(embedding)));
            command.Parameters.AddWithValue("userId", userId);
            command.Parameters.AddWithValue("topN", topN);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var results = new List<AIMemorySearchResult>();

            while (await reader.ReadAsync(cancellationToken))
            {
                DateTime? updatedUtc = null;

                if (!reader.IsDBNull(4))
                {
                    updatedUtc = reader.GetDateTime(4);
                }

                var content = reader.IsDBNull(3) ? null : reader.GetString(3);

                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                results.Add(new AIMemorySearchResult
                {
                    MemoryId = reader.IsDBNull(0) ? null : reader.GetString(0),
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Content = content,
                    UpdatedUtc = updatedUtc,
                    Score = reader.IsDBNull(5) ? 0f : reader.GetFloat(5),
                });
            }

            return results
                .OrderByDescending(x => x.Score)
                .Take(topN)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing AI memory search in PostgreSQL table '{IndexName}'.", tableName);

            return [];
        }
    }
}
