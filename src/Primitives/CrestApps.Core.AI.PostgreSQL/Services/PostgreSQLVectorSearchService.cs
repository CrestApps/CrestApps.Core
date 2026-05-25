using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.PostgreSQL;
using CrestApps.Core.PostgreSQL.Services;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CrestApps.Core.AI.PostgreSQL.Services;

/// <summary>
/// PostgreSQL implementation of <see cref="IVectorSearchService"/> for searching document embeddings.
/// Uses pgvector cosine distance for k-NN search on flat document chunks.
/// </summary>
internal sealed class PostgreSQLVectorSearchService : IVectorSearchService
{
    private readonly IPostgreSQLClientFactory _clientFactory;
    private readonly ILogger<PostgreSQLVectorSearchService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLVectorSearchService"/> class.
    /// </summary>
    /// <param name="clientFactory">The PostgreSQL client factory.</param>
    /// <param name="logger">The logger.</param>
    public PostgreSQLVectorSearchService(
        IPostgreSQLClientFactory clientFactory,
        ILogger<PostgreSQLVectorSearchService> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Searches for document chunks similar to the provided embedding vector,
    /// filtered by reference ID and reference type.
    /// </summary>
    /// <param name="indexProfile">The index profile describing the target index.</param>
    /// <param name="embedding">The embedding vector to search against.</param>
    /// <param name="referenceId">The reference entity identifier to scope the search.</param>
    /// <param name="referenceType">The type of the reference entity.</param>
    /// <param name="topN">The maximum number of results to return.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<IEnumerable<DocumentChunkSearchResult>> SearchAsync(
        IIndexProfileInfo indexProfile,
        float[] embedding,
        string referenceId,
        string referenceType,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexProfile);
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceType);

        if (embedding.Length == 0)
        {
            return [];
        }

        var tableName = PostgreSQLHelpers.SanitizeTableName(indexProfile.IndexFullName);
        var quotedTableName = PostgreSQLHelpers.QuoteIdentifier(tableName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = $"""
                SELECT {PostgreSQLHelpers.SanitizeColumnName(DocumentIndexConstants.ColumnNames.Content)},
                       {PostgreSQLHelpers.SanitizeColumnName(DocumentIndexConstants.ColumnNames.ChunkIndex)},
                       {PostgreSQLHelpers.SanitizeColumnName(DocumentIndexConstants.ColumnNames.DocumentId)},
                       {PostgreSQLHelpers.SanitizeColumnName(DocumentIndexConstants.ColumnNames.FileName)},
                       1 - ({PostgreSQLHelpers.SanitizeColumnName(DocumentIndexConstants.ColumnNames.Embedding)} <=> @embedding) AS score
                FROM {quotedTableName}
                WHERE {PostgreSQLHelpers.SanitizeColumnName(DocumentIndexConstants.ColumnNames.ReferenceId)} = @referenceId
                    AND {PostgreSQLHelpers.SanitizeColumnName(DocumentIndexConstants.ColumnNames.ReferenceType)} = @referenceType
                ORDER BY {PostgreSQLHelpers.SanitizeColumnName(DocumentIndexConstants.ColumnNames.Embedding)} <=> @embedding
                LIMIT @topN
                """;

            command.Parameters.Add(new NpgsqlParameter("embedding", new Pgvector.Vector(embedding)));
            command.Parameters.AddWithValue("referenceId", referenceId);
            command.Parameters.AddWithValue("referenceType", referenceType);
            command.Parameters.AddWithValue("topN", topN);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var results = new List<DocumentChunkSearchResult>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var chunkText = reader.IsDBNull(0) ? null : reader.GetString(0);

                if (string.IsNullOrEmpty(chunkText))
                {
                    continue;
                }

                var chunkIndex = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var documentKey = reader.IsDBNull(2) ? null : reader.GetString(2);
                var fileName = reader.IsDBNull(3) ? null : reader.GetString(3);
                var score = reader.IsDBNull(4) ? 0f : reader.GetFloat(4);

                results.Add(new DocumentChunkSearchResult
                {
                    Chunk = new ChatInteractionDocumentChunk
                    {
                        Text = chunkText,
                        Index = chunkIndex,
                    },
                    DocumentKey = documentKey,
                    FileName = fileName,
                    Score = score,
                });
            }

            return results
                .OrderByDescending(r => r.Score)
                .Take(topN)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing vector search in PostgreSQL table '{IndexName}'.", tableName);

            return [];
        }
    }
}
