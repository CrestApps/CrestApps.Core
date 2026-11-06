using System.Text.Json.Nodes;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Elasticsearch.Services;

/// <summary>
/// Elasticsearch implementation of <see cref="IVectorSearchService"/> for searching document embeddings.
/// Uses k-NN (k-Nearest Neighbors) search on flat document chunks with vector similarity.
/// </summary>
internal sealed class ElasticsearchVectorSearchService : IVectorSearchService
{
    private readonly ElasticsearchClient _elasticClient;
    private readonly DocumentIndexColumnNames _columnNames;
    private readonly ILogger<ElasticsearchVectorSearchService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticsearchVectorSearchService"/> class.
    /// </summary>
    /// <param name="elasticClient">The elastic client.</param>
    /// <param name="columnNames">The index field names, or <see langword="null"/> for <see cref="DocumentIndexColumnNames.Default"/>.</param>
    /// <param name="logger">The logger.</param>
    public ElasticsearchVectorSearchService(
        ElasticsearchClient elasticClient,
        DocumentIndexColumnNames columnNames,
        ILogger<ElasticsearchVectorSearchService> logger)
    {
        _elasticClient = elasticClient;
        _columnNames = columnNames ?? DocumentIndexColumnNames.Default;
        _logger = logger;
    }

    /// <summary>
    /// Searchs the operation.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="embedding">The embedding.</param>
    /// <param name="referenceId">The reference id.</param>
    /// <param name="referenceType">The reference type.</param>
    /// <param name="topN">The top n.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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

        try
        {
            var response = await _elasticClient.SearchAsync<JsonObject>(s => s
                .Indices(indexProfile.IndexFullName)
                .Knn(k => k
                    .Field(_columnNames.Embedding)
                    .QueryVector(embedding)
                    .K(topN)
                    .NumCandidates(topN * 10)
                    .Filter(f => f
                        .Bool(b => b
                            .Must(
                                m => m.Term(t => t
                                    .Field(_columnNames.ReferenceId)
                                    .Value(referenceId)),
                                m => m.Term(t => t
                                    .Field(_columnNames.ReferenceType)
                                    .Value(referenceType))))))
                .Size(topN),
                cancellationToken);

            if (!response.IsValidResponse)
            {
                _logger.LogWarning("Elasticsearch vector search failed: {Error}", response.DebugInformation);

                return [];
            }

            var results = new List<DocumentChunkSearchResult>();
            var documents = response.Documents.GetEnumerator();
            var hits = response.Hits.GetEnumerator();

            while (documents.MoveNext() && hits.MoveNext())
            {
                var hit = hits.Current;
                var document = documents.Current;

                if (document == null)
                {
                    continue;
                }

                var chunkText = document.TryGetPropertyValue(_columnNames.Content, out var textNode)
                    ? textNode?.GetValue<string>()
                    : null;
                var chunkIndex = 0;

                if (document.TryGetPropertyValue(_columnNames.ChunkIndex, out var indexNode) && indexNode != null)
                {
                    chunkIndex = indexNode.GetValue<int>();
                }

                if (!string.IsNullOrEmpty(chunkText))
                {
                    var documentKey = document.TryGetPropertyValue(_columnNames.DocumentId, out var docIdNode)
                        ? docIdNode?.GetValue<string>()
                        : null;
                    var fileName = document.TryGetPropertyValue(_columnNames.FileName, out var fileNameNode)
                        ? fileNameNode?.GetValue<string>()
                        : null;

                    results.Add(new DocumentChunkSearchResult
                    {
                        Chunk = new ChatInteractionDocumentChunk
                        {
                            Text = chunkText,
                            Index = chunkIndex,
                        },
                        DocumentKey = documentKey,
                        FileName = fileName,
                        Score = (float)(hit.Score ?? 0.0),
                    });
                }
            }

            return results
                .OrderByDescending(r => r.Score)
                .Take(topN)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing vector search in Elasticsearch index '{IndexName}'.", indexProfile.IndexFullName);

            return [];
        }
    }
}
