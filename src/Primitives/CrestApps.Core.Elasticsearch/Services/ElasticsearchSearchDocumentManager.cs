using System.Text.Json.Nodes;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Elasticsearch.Services;

/// <summary>
/// Elasticsearch implementation of <see cref = "ISearchDocumentManager"/>
/// for adding, updating, and deleting documents in search indexes.
/// </summary>
internal sealed class ElasticsearchSearchDocumentManager : ISearchDocumentManager
{
    private readonly ElasticsearchClient _elasticClient;
    private readonly IEnumerable<ISearchDocumentHandler> _handlers;
    private readonly ILogger _logger;

    public ElasticsearchSearchDocumentManager(
        ElasticsearchClient elasticClient,
        IEnumerable<ISearchDocumentHandler> handlers,
        ILogger<ElasticsearchSearchDocumentManager> logger)
    {
        _elasticClient = elasticClient;
        _handlers = handlers;
        _logger = logger;
    }

    private static string SanitizeLogValue(string value)
    {
        return value?.Replace("\r", string.Empty).Replace("\n", string.Empty) ?? string.Empty;
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
            var operations = new List<IBulkOperation>();
            foreach (var document in documents)
            {
                var jsonDoc = new JsonObject();
                foreach (var field in document.Fields)
                {
                    jsonDoc[field.Key] = JsonValue.Create(field.Value);
                }

                operations.Add(new BulkIndexOperation<JsonObject>(jsonDoc) { Id = document.Id, Index = profile.IndexFullName, });
            }

            var request = new BulkRequest
            {
                Operations = operations,
            };
            var response = await _elasticClient.BulkAsync(request, cancellationToken);
            if (!response.IsValidResponse)
            {
                _logger.LogWarning("Elasticsearch bulk index failed for index '{IndexName}'.", SanitizeLogValue(profile.IndexFullName));
                return false;
            }

            await NotifyDocumentsAddedOrUpdatedAsync(profile, documents, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing documents in Elasticsearch index '{IndexName}'.", SanitizeLogValue(profile.IndexFullName));
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
            var operations = new List<IBulkOperation>();
            foreach (var id in ids)
            {
                operations.Add(new BulkDeleteOperation(id) { Index = profile.IndexFullName, });
            }

            var request = new BulkRequest
            {
                Operations = operations,
            };
            var response = await _elasticClient.BulkAsync(request, cancellationToken);
            if (!response.IsValidResponse)
            {
                _logger.LogWarning("Elasticsearch bulk delete failed for index '{IndexName}'.", SanitizeLogValue(profile.IndexFullName));
            }

            await NotifyDocumentsDeletedAsync(profile, ids, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting documents from Elasticsearch index '{IndexName}'.", SanitizeLogValue(profile.IndexFullName));
        }
    }

    public async Task DeleteAllAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            var response = await _elasticClient.DeleteByQueryAsync<JsonObject>(profile.IndexFullName, d => d.Query(q => q.MatchAll(m =>
            {
            })), cancellationToken);
            if (!response.IsValidResponse)
            {
                _logger.LogWarning("Elasticsearch delete all failed for index '{IndexName}'.", SanitizeLogValue(profile.IndexFullName));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting all documents from Elasticsearch index '{IndexName}'.", SanitizeLogValue(profile.IndexFullName));
        }
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
            _logger.LogTrace("Notifying {HandlerCount} search document handler(s) after add/update for index '{IndexName}' with {DocumentCount} document id(s).", handlers.Length, SanitizeLogValue(profile.IndexFullName), documentIds.Length);
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler.DocumentsAddedOrUpdatedAsync(profile, documentIds, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search document handler '{HandlerType}' failed after indexing documents into '{IndexName}'.", handler.GetType().Name, SanitizeLogValue(profile.IndexFullName));
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
            _logger.LogTrace("Notifying {HandlerCount} search document handler(s) after delete for index '{IndexName}' with {DocumentCount} document id(s).", handlers.Length, SanitizeLogValue(profile.IndexFullName), documentIds.Count);
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler.DocumentsDeletedAsync(profile, documentIds, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search document handler '{HandlerType}' failed after deleting documents from '{IndexName}'.", handler.GetType().Name, SanitizeLogValue(profile.IndexFullName));
            }
        }
    }
}
