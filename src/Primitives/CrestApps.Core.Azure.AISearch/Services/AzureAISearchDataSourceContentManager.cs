using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Azure.AISearch.Services;

/// <summary>
/// Azure AI Search implementation of <see cref="IDataSourceContentManager"/>
/// for searching data source embedding indexes using vector similarity.
/// </summary>
internal sealed class AzureAISearchDataSourceContentManager : IDataSourceContentManager
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly ILogger<AzureAISearchDataSourceContentManager> _logger;

    internal static string BuildODataFilter(string dataSourceId, string filter)
    {
        // Always filter by dataSourceId.
        var odataFilter = $"{DataSourceConstants.ColumnNames.DataSourceId} eq '{SanitizeODataValue(dataSourceId)}'";

        // Merge with user-provided filter (already translated to OData for Azure).

        if (!string.IsNullOrWhiteSpace(filter))
        {
            odataFilter = $"({odataFilter}) and ({filter})";
        }

        return odataFilter;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAISearchDataSourceContentManager"/> class.
    /// </summary>
    /// <param name="searchIndexClient">The search index client.</param>
    /// <param name="logger">The logger.</param>
    public AzureAISearchDataSourceContentManager(
        SearchIndexClient searchIndexClient,
        ILogger<AzureAISearchDataSourceContentManager> logger)
    {
        _searchIndexClient = searchIndexClient;
        _logger = logger;
    }

    /// <summary>
    /// Searchs the operation.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="embedding">The embedding.</param>
    /// <param name="dataSourceId">The data source id.</param>
    /// <param name="topN">The top n.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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

        try
        {
            var searchClient = _searchIndexClient.GetSearchClient(indexProfile.IndexFullName);

            var vectorQuery = new VectorizedQuery(embedding)
            {
                KNearestNeighborsCount = topN,
                Fields =
                {
                    DataSourceConstants.ColumnNames.Embedding,
                }
            };

            var odataFilter = BuildODataFilter(dataSourceId, filter);

            var searchOptions = new SearchOptions
            {
                Filter = odataFilter,
                Size = topN,
                Select =
                {
                    DataSourceConstants.ColumnNames.ChunkId,
                    DataSourceConstants.ColumnNames.ReferenceId,
                    DataSourceConstants.ColumnNames.DataSourceId,
                    DataSourceConstants.ColumnNames.ReferenceType,
                    DataSourceConstants.ColumnNames.Title,
                    DataSourceConstants.ColumnNames.Content,
                    DataSourceConstants.ColumnNames.ChunkIndex,
                },
                VectorSearch = new VectorSearchOptions
                {
                    Queries = { vectorQuery },
                }
            };

            var response = await searchClient.SearchAsync<SearchDocument>(
                searchText: null,
                searchOptions,
                cancellationToken);

            var results = new List<DataSourceSearchResult>();

            await foreach (var result in response.Value.GetResultsAsync())
            {
                var document = result.Document;

                var referenceId = document.TryGetValue(DataSourceConstants.ColumnNames.ReferenceId, out var refObj)
                    ? refObj?.ToString()
                    : null;

                var title = document.TryGetValue(DataSourceConstants.ColumnNames.Title, out var titleObj)
                    ? titleObj?.ToString()
                    : null;

                var content = document.TryGetValue(DataSourceConstants.ColumnNames.Content, out var contentObj)
                    ? contentObj?.ToString()
                    : null;

                var chunkIndex = 0;

                if (document.TryGetValue(DataSourceConstants.ColumnNames.ChunkIndex, out var chunkIndexObj))
                {
                    if (chunkIndexObj is int intValue)
                    {
                        chunkIndex = intValue;
                    }
                    else if (int.TryParse(chunkIndexObj?.ToString(), out var parsedIndex))
                    {
                        chunkIndex = parsedIndex;
                    }
                }

                var referenceType = document.TryGetValue(DataSourceConstants.ColumnNames.ReferenceType, out var refTypeObj)
                    ? refTypeObj?.ToString()
                    : null;

                if (!string.IsNullOrEmpty(content))
                {
                    results.Add(new DataSourceSearchResult
                    {
                        ReferenceId = referenceId,
                        Title = title,
                        Content = content,
                        ChunkIndex = chunkIndex,
                        ReferenceType = referenceType,
                        Score = (float)(result.Score ?? 0.0)
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
            _logger.LogError(ex, "Error performing data source vector search in Azure AI Search index '{IndexName}'",
                indexProfile.IndexFullName);

            return [];
        }
    }

    /// <summary>
    /// Deletes by data source id.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="dataSourceId">The data source id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<long> DeleteByDataSourceIdAsync(
        IIndexProfileInfo indexProfile,
        string dataSourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);

        try
        {
            var searchClient = _searchIndexClient.GetSearchClient(indexProfile.IndexFullName);

            var odataFilter = $"{DataSourceConstants.ColumnNames.DataSourceId} eq '{SanitizeODataValue(dataSourceId)}'";

            long totalDeleted = 0;

            // Paginate through all matching documents and batch-delete them.
            while (!cancellationToken.IsCancellationRequested)
            {
                var searchOptions = new SearchOptions
                {
                    Filter = odataFilter,
                    Size = 1000,
                    Select = { DataSourceConstants.ColumnNames.ChunkId },
                };

                var response = await searchClient.SearchAsync<SearchDocument>(
                    searchText: "*",
                    searchOptions,
                    cancellationToken);

                var keysToDelete = new List<string>();

                await foreach (var result in response.Value.GetResultsAsync())
                {
                    if (result.Document.TryGetValue(DataSourceConstants.ColumnNames.ChunkId, out var chunkIdObj)
                        && chunkIdObj?.ToString() is string chunkId
                            && !string.IsNullOrEmpty(chunkId))
                    {
                        keysToDelete.Add(chunkId);
                    }
                }

                if (keysToDelete.Count == 0)
                {
                    break;
                }

                var batch = IndexDocumentsBatch.Delete(
                    DataSourceConstants.ColumnNames.ChunkId,
                    keysToDelete);

                await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

                totalDeleted += keysToDelete.Count;

                // If we got fewer results than the page size, all matching documents have been processed.

                if (keysToDelete.Count < 1000)
                {
                    break;
                }
            }

            return totalDeleted;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search delete by data source ID failed for index '{IndexName}': {Message}",
                indexProfile.IndexFullName, ex.Message);

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting documents by data source ID '{DataSourceId}' from Azure AI Search index '{IndexName}'.",
                dataSourceId, indexProfile.IndexFullName);

            return 0;
        }
    }

    private static string SanitizeODataValue(string value)
    {
        return value.Replace("'", "''");
    }
}
