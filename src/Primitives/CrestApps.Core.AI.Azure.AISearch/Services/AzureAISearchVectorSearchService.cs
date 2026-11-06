using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.Logging;
using AzureSearchDocument = Azure.Search.Documents.Models.SearchDocument;

namespace CrestApps.Core.AI.Azure.AISearch.Services;

/// <summary>
/// Azure AI Search implementation of <see cref="IVectorSearchService"/> for searching document embeddings.
/// Uses vector search on flat document chunks with similarity scoring.
/// </summary>
internal sealed class AzureAISearchVectorSearchService : IVectorSearchService
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly DocumentIndexColumnNames _columnNames;
    private readonly ILogger<AzureAISearchVectorSearchService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAISearchVectorSearchService"/> class.
    /// </summary>
    /// <param name="searchIndexClient">The search index client.</param>
    /// <param name="columnNames">The index field names, or <see langword="null"/> for <see cref="DocumentIndexColumnNames.Default"/>.</param>
    /// <param name="logger">The logger.</param>
    public AzureAISearchVectorSearchService(
        SearchIndexClient searchIndexClient,
        DocumentIndexColumnNames columnNames,
        ILogger<AzureAISearchVectorSearchService> logger)
    {
        _searchIndexClient = searchIndexClient;
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
            var searchClient = _searchIndexClient.GetSearchClient(indexProfile.IndexFullName);

            var vectorQuery = new VectorizedQuery(embedding)
            {
                KNearestNeighborsCount = topN,
                Fields =
                {
                    _columnNames.Embedding,
                },
            };

            var searchOptions = new SearchOptions
            {
                Filter = $"{ODataFilter.Equal(_columnNames.ReferenceId, referenceId)} and {ODataFilter.Equal(_columnNames.ReferenceType, referenceType)}",
                Size = topN,
                Select =
                {
                    _columnNames.ChunkId,
                    _columnNames.DocumentId,
                    _columnNames.Content,
                    _columnNames.FileName,
                    _columnNames.ChunkIndex,
                },
                VectorSearch = new VectorSearchOptions
                {
                    Queries = { vectorQuery },
                },
            };

            var response = await searchClient.SearchAsync<AzureSearchDocument>(
                searchText: null,
                searchOptions,
                cancellationToken);

            var results = new List<DocumentChunkSearchResult>();

            await foreach (var result in response.Value.GetResultsAsync())
            {
                var document = result.Document;

                var chunkText = document.TryGetValue(_columnNames.Content, out var textObj)
                    ? textObj?.ToString()
                    : null;

                var chunkIndex = 0;

                if (document.TryGetValue(_columnNames.ChunkIndex, out var indexObj))
                {
                    if (indexObj is int intValue)
                    {
                        chunkIndex = intValue;
                    }
                    else if (int.TryParse(indexObj?.ToString(), out var parsedIndex))
                    {
                        chunkIndex = parsedIndex;
                    }
                }

                if (!string.IsNullOrEmpty(chunkText))
                {
                    var documentKey = document.TryGetValue(_columnNames.DocumentId, out var docIdObj)
                        ? docIdObj?.ToString()
                        : null;

                    var fileName = document.TryGetValue(_columnNames.FileName, out var fileNameObj)
                        ? fileNameObj?.ToString()
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
                        Score = (float)(result.Score ?? 0.0),
                    });
                }
            }

            return results
                .OrderByDescending(r => r.Score)
                .Take(topN)
                .ToList();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search request failed for index '{IndexName}': {Message}",
                indexProfile.IndexFullName, ex.Message);

            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing vector search in Azure AI Search index '{IndexName}'.",
                indexProfile.IndexFullName);

            return [];
        }
    }
}
