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
    private const int ReferenceIdsPerFilter = 100;

    private const int DeletePageSize = 1000;

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
                    DataSourceConstants.ColumnNames.ContentType,
                    DataSourceConstants.ColumnNames.RootId,
                    DataSourceConstants.ColumnNames.ParentId,
                    DataSourceConstants.ColumnNames.Page,
                },
                VectorSearch = new VectorSearchOptions
                {
                    Queries = { vectorQuery },
                }
            };

            Response<SearchResults<SearchDocument>> response;

            try
            {
                response = await searchClient.SearchAsync<SearchDocument>(
                    searchText: null,
                    searchOptions,
                    cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 400)
            {
                // An index created before typed knowledge existed has no such fields, and selecting one it
                // does not have is rejected outright. Searching without them returns rows that read as
                // ordinary text, which is what they are.
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Index '{IndexName}' does not expose the typed knowledge fields. Searching without them until it is re-created.",
                        indexProfile.IndexFullName);
                }

                foreach (var name in new[] { DataSourceConstants.ColumnNames.ContentType, DataSourceConstants.ColumnNames.RootId, DataSourceConstants.ColumnNames.ParentId, DataSourceConstants.ColumnNames.Page })
                {
                    searchOptions.Select.Remove(name);
                }

                response = await searchClient.SearchAsync<SearchDocument>(
                    searchText: null,
                    searchOptions,
                    cancellationToken);
            }

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
                        DataSourceId = dataSourceId,
                        Title = title,
                        Content = content,
                        ChunkIndex = chunkIndex,
                        ReferenceType = referenceType,
                        Score = (float)(result.Score ?? 0.0),
                        ContentType = ReadString(document, DataSourceConstants.ColumnNames.ContentType) ?? KnowledgeContentTypes.Text,
                        RootId = ReadString(document, DataSourceConstants.ColumnNames.RootId),
                        ParentId = ReadString(document, DataSourceConstants.ColumnNames.ParentId),
                        Page = ReadInt32(document, DataSourceConstants.ColumnNames.Page),
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
    /// Reads one string field, tolerating its absence.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or <see langword="null"/> when the field is not present.</returns>
    private static string ReadString(SearchDocument document, string name)
    {
        return document.TryGetValue(name, out var value) ? value?.ToString() : null;
    }

    /// <summary>
    /// Reads one integer field, tolerating its absence.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or <see langword="null"/> when the field is not present.</returns>
    private static int? ReadInt32(SearchDocument document, string name)
    {
        if (!document.TryGetValue(name, out var value) || value == null)
        {
            return null;
        }

        if (value is int number)
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Deletes every row belonging to the supplied reference identifiers.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="referenceIds">The reference identifiers to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the delete ran.</returns>
    /// <remarks>
    /// The service has no delete-by-query, so the matching keys are searched for and then deleted. That is
    /// still far less work than guessing a thousand chunk identifiers per reference. The list is batched
    /// because a filter expression has a length limit.
    /// </remarks>
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

        try
        {
            var searchClient = _searchIndexClient.GetSearchClient(indexProfile.IndexFullName);

            foreach (var batch in referenceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Chunk(ReferenceIdsPerFilter))
            {
                await DeleteByFilterAsync(searchClient, BuildReferenceIdFilter(dataSourceId, batch), cancellationToken);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Returning false lets the caller fall back to deleting by identifier list.
            _logger.LogWarning(ex, "Error deleting by reference id in Azure AI Search index '{IndexName}'.", indexProfile.IndexFullName);

            return false;
        }
    }

    /// <summary>
    /// Searches for the rows a filter matches and deletes them, a page at a time.
    /// </summary>
    /// <param name="searchClient">The search client.</param>
    /// <param name="filter">The OData filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task DeleteByFilterAsync(SearchClient searchClient, string filter, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await searchClient.SearchAsync<SearchDocument>(
                searchText: "*",
                new SearchOptions
                {
                    Filter = filter,
                    Size = DeletePageSize,
                    Select = { DataSourceConstants.ColumnNames.ChunkId },
                },
                cancellationToken);

            var keys = new List<string>();

            await foreach (var result in response.Value.GetResultsAsync())
            {
                if (result.Document.TryGetValue(DataSourceConstants.ColumnNames.ChunkId, out var chunkIdObj) &&
                    chunkIdObj?.ToString() is string chunkId &&
                    !string.IsNullOrEmpty(chunkId))
                {
                    keys.Add(chunkId);
                }
            }

            if (keys.Count == 0)
            {
                return;
            }

            await searchClient.IndexDocumentsAsync(
                IndexDocumentsBatch.Delete(DataSourceConstants.ColumnNames.ChunkId, keys),
                cancellationToken: cancellationToken);

            if (keys.Count < DeletePageSize)
            {
                return;
            }
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

    /// <summary>
    /// Builds the filter that selects one batch of references within one data source.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="referenceIds">The references in this batch.</param>
    /// <returns>The OData filter.</returns>
    /// <remarks>
    /// Scoped to the data source as well as the references, so an identifier that happens to be shared by
    /// two data sources can never delete the wrong one's rows. Values are escaped because a reference
    /// identifier is whatever the source called the item, apostrophes and all.
    /// <para>
    /// The references are compared one by one rather than through <c>search.in</c>. That function takes a
    /// single delimited string, not a list of arguments, and its default delimiters include the space and
    /// the comma — both of which occur in ordinary reference identifiers such as a file name. Passing a
    /// delimiter of our own only moves the problem to whichever character we picked, whereas a chain of
    /// equality tests cannot be broken by any value at all. A batch is bounded, so the filter stays short.
    /// </para>
    /// </remarks>
    internal static string BuildReferenceIdFilter(string dataSourceId, IEnumerable<string> referenceIds)
    {
        var comparisons = string.Join(
            " or ",
            referenceIds.Select(id => $"{DataSourceConstants.ColumnNames.ReferenceId} eq '{SanitizeODataValue(id)}'"));

        return $"{DataSourceConstants.ColumnNames.DataSourceId} eq '{SanitizeODataValue(dataSourceId)}' and ({comparisons})";
    }

    /// <summary>
    /// Gets how many reference identifiers go into one filter.
    /// </summary>
    internal static int ReferenceIdBatchSize => ReferenceIdsPerFilter;

    private static string SanitizeODataValue(string value)
    {
        return value.Replace("'", "''");
    }
}
