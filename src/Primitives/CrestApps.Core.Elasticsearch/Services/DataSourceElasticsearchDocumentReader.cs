using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Support;
using Elastic.Clients.Elasticsearch;

namespace CrestApps.Core.Elasticsearch.Services;

/// <summary>
/// Reads documents from an Elasticsearch source index.
/// </summary>
internal sealed class DataSourceElasticsearchDocumentReader : IDataSourceDocumentReader
{
    private const int BatchSize = 1000;

    private readonly ElasticsearchClient _elasticClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSourceElasticsearchDocumentReader"/> class.
    /// </summary>
    /// <param name="elasticClient">The elastic client.</param>
    public DataSourceElasticsearchDocumentReader(ElasticsearchClient elasticClient)
    {
        _elasticClient = elasticClient;
    }

    /// <summary>
    /// Reads the operation.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="keyFieldName">The key field name.</param>
    /// <param name="titleFieldName">The title field name.</param>
    /// <param name="contentFieldName">The content field name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(
        IIndexProfileInfo indexProfile,
        string keyFieldName,
        string titleFieldName,
        string contentFieldName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (indexProfile == null)
        {
            yield break;
        }

        string searchAfterValue = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var response = searchAfterValue == null
            ? await _elasticClient.SearchAsync<JsonObject>(s => s
                .Indices(indexProfile.IndexFullName)
                .Size(BatchSize)
                .Sort(sort => sort.Field("_doc"))
            , cancellationToken)
            : await _elasticClient.SearchAsync<JsonObject>(s => s
                .Indices(indexProfile.IndexFullName)
                .Size(BatchSize)
                .Sort(sort => sort.Field("_doc"))
                .SearchAfter([searchAfterValue])
            , cancellationToken);

            if (!response.IsValidResponse || response.Hits.Count == 0)
            {
                break;
            }

            foreach (var hit in response.Hits)
            {
                if (hit.Id == null || hit.Source == null)
                {
                    continue;
                }

                var key = hit.Id;

                if (!string.IsNullOrEmpty(keyFieldName))
                {
                    var keyNode = ResolveFieldValue(hit.Source, keyFieldName);

                    if (keyNode != null)
                    {
                        key = keyNode.GetStringValue() ?? key;
                    }
                }

                yield return new KeyValuePair<string, SourceDocument>(
                    key, ExtractDocument(hit.Source, titleFieldName, contentFieldName));
            }

            var lastSort = response.Hits.Last().Sort;
            searchAfterValue = lastSort != null && lastSort.Count > 0
            ? lastSort.First().ToString()
            : null;

            if (string.IsNullOrEmpty(searchAfterValue) || response.Hits.Count < BatchSize)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Reads by ids.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="keyFieldName">The key field name.</param>
    /// <param name="titleFieldName">The title field name.</param>
    /// <param name="contentFieldName">The content field name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(
        IIndexProfileInfo indexProfile,
        IEnumerable<string> documentIds,
        string keyFieldName,
        string titleFieldName,
        string contentFieldName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (indexProfile == null || documentIds == null)
        {
            yield break;
        }

        var idList = documentIds.Where(id => !string.IsNullOrEmpty(id)).ToList();

        if (idList.Count == 0)
        {
            yield break;
        }

        // Use a search query with IDs filter to fetch specific documents.
        var response = await _elasticClient.SearchAsync<JsonObject>(s => s
            .Indices(indexProfile.IndexFullName)
            .Query(q => q
            .Ids(ids => ids.Values(new Ids(idList)))
        )
            .Size(idList.Count)
        , cancellationToken);

        if (!response.IsValidResponse || response.Hits == null)
        {
            yield break;
        }

        foreach (var hit in response.Hits)
        {
            if (hit.Source == null)
            {
                continue;
            }

            var nativeId = hit.Id;

            var key = nativeId;

            if (!string.IsNullOrEmpty(keyFieldName))
            {
                var keyNode = ResolveFieldValue(hit.Source, keyFieldName);

                if (keyNode != null)
                {
                    key = keyNode.GetStringValue() ?? key;
                }
            }

            yield return new KeyValuePair<string, SourceDocument>(
                key, ExtractDocument(hit.Source, titleFieldName, contentFieldName));
        }
    }

    private static SourceDocument ExtractDocument(JsonObject source, string titleFieldName, string contentFieldName)
    {
        string title = null;
        string content = null;

        if (!string.IsNullOrEmpty(titleFieldName))
        {
            var titleNode = ResolveFieldValue(source, titleFieldName);
            title = titleNode.GetStringValue();
        }

        if (!string.IsNullOrEmpty(contentFieldName))
        {
            var contentNode = ResolveFieldValue(source, contentFieldName);
            content = contentNode.GetStringValue();
        }

        if (string.IsNullOrEmpty(content))
        {
            // Fallback: use the full document JSON as content.
            content = source.ToJsonString();
        }

        if (string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(content))
        {
            title = content.ExtractTitleFromContent();
        }

        // Populate all source fields for filter field propagation.
        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in source)
        {
            fields[prop.Key] = prop.Value.GetRawValue();
        }

        return new SourceDocument
        {
            Title = title,
            Content = content,
            Fields = fields,
        };
    }

    /// <summary>
    /// Resolves a field value from a JSON object using a dotted path (e.g., "Content.ContentItem.DisplayText").
    /// Falls back to a direct property lookup if the path has no dots.
    /// </summary>
    private static JsonNode ResolveFieldValue(JsonObject source, string fieldPath)
    {
        if (source == null || string.IsNullOrEmpty(fieldPath))
        {
            return null;
        }

        // Try direct property lookup first (handles flat field names).

        if (source.TryGetPropertyValue(fieldPath, out var directNode))
        {
            return directNode;
        }

        // Traverse dotted path for nested objects.

        if (!fieldPath.Contains('.'))
        {
            return null;
        }

        var segments = fieldPath.Split('.');
        JsonNode current = source;

        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }
}
