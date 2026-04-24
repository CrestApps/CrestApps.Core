using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Support;
using Microsoft.Extensions.Logging;
using AzureSearchDocument = Azure.Search.Documents.Models.SearchDocument;

namespace CrestApps.Core.Azure.AISearch.Services;

/// <summary>
/// Azure AI Search implementation of <see cref = "ISearchDocumentManager"/>
/// for adding, updating, and deleting documents in search indexes.
/// </summary>
internal sealed class AzureAISearchDocumentManager : ISearchDocumentManager
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IEnumerable<ISearchDocumentHandler> _handlers;
    private readonly ILogger _logger;

    public AzureAISearchDocumentManager(
        SearchIndexClient searchIndexClient,
        IEnumerable<ISearchDocumentHandler> handlers,
        ILogger<AzureAISearchDocumentManager> logger)
    {
        _searchIndexClient = searchIndexClient;
        _handlers = handlers;
        _logger = logger;
    }

    public async Task<bool> AddOrUpdateAsync(IIndexProfileInfo profile, IReadOnlyCollection<IndexDocument> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
        {
            return true;
        }

        try
        {
            var searchClient = _searchIndexClient.GetSearchClient(profile.IndexFullName);
            var azureDocs = new List<AzureSearchDocument>();
            foreach (var document in documents)
            {
                var azureDoc = new AzureSearchDocument();
                foreach (var field in document.Fields)
                {
                    azureDoc[field.Key] = field.Value;
                }

                azureDocs.Add(azureDoc);
            }

            var batch = IndexDocumentsBatch.MergeOrUpload(azureDocs);
            await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

            await NotifyDocumentsAddedOrUpdatedAsync(profile, documents, cancellationToken);

            return true;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search index documents failed for index '{IndexName}'.", profile.IndexFullName.SanitizeForLog());
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing documents in Azure AI Search index '{IndexName}'.", profile.IndexFullName.SanitizeForLog());
            return false;
        }
    }

    public async Task DeleteAsync(IIndexProfileInfo profile, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(documentIds);
        var ids = documentIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        try
        {
            var searchClient = _searchIndexClient.GetSearchClient(profile.IndexFullName);
            // Determine the key field name from an existing document or default.
            var keyFieldName = await GetKeyFieldNameAsync(profile.IndexFullName, cancellationToken);
            var batch = IndexDocumentsBatch.Delete(keyFieldName, ids);
            await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

            await NotifyDocumentsDeletedAsync(profile, ids, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search delete failed for index '{IndexName}'.", profile.IndexFullName.SanitizeForLog());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting documents from Azure AI Search index '{IndexName}'.", profile.IndexFullName.SanitizeForLog());
        }
    }

    public async Task DeleteAllAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            var searchClient = _searchIndexClient.GetSearchClient(profile.IndexFullName);
            var keyFieldName = await GetKeyFieldNameAsync(profile.IndexFullName, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var searchOptions = new SearchOptions
                {
                    Size = 1000,
                    Select =
                    {
                        keyFieldName
                    },
                };
                var response = await searchClient.SearchAsync<AzureSearchDocument>(searchText: "*", searchOptions, cancellationToken);
                var keysToDelete = new List<string>();
                await foreach (var result in response.Value.GetResultsAsync())
                {
                    if (result.Document.TryGetValue(keyFieldName, out var keyObj) && keyObj?.ToString() is string key && !string.IsNullOrEmpty(key))
                    {
                        keysToDelete.Add(key);
                    }
                }

                if (keysToDelete.Count == 0)
                {
                    break;
                }

                var batch = IndexDocumentsBatch.Delete(keyFieldName, keysToDelete);
                await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);
                if (keysToDelete.Count < 1000)
                {
                    break;
                }
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search delete all failed for index '{IndexName}'.", profile.IndexFullName.SanitizeForLog());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting all documents from Azure AI Search index '{IndexName}'.", profile.IndexFullName.SanitizeForLog());
        }
    }

    private async Task<string> GetKeyFieldNameAsync(string indexFullName, CancellationToken cancellationToken)
    {
        try
        {
            var index = await _searchIndexClient.GetIndexAsync(indexFullName, cancellationToken);
            var keyField = index.Value.Fields.FirstOrDefault(f => f.IsKey == true);
            if (keyField != null)
            {
                return keyField.Name;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to determine key field for index '{IndexName}', defaulting to 'id'.", indexFullName.SanitizeForLog());
        }

        return "id";
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
