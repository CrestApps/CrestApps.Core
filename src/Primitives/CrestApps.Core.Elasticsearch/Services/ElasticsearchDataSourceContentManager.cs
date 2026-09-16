using System.Text;
using System.Text.Json.Nodes;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;

using Elastic.Clients.Elasticsearch;

using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Elasticsearch.Services;

/// <summary>
/// Elasticsearch implementation of <see cref="IDataSourceContentManager"/>
/// for searching data source embedding indexes using k-NN vector similarity.
/// </summary>
internal sealed class ElasticsearchDataSourceContentManager : IDataSourceContentManager
{
    private readonly ElasticsearchClient _elasticClient;
    private readonly ILogger<ElasticsearchDataSourceContentManager> _logger;

    internal static List<(string Kind, string Value)> BuildMustQueryDebug(string dataSourceId, string filter)
    {
        var list = new List<(string Kind, string Value)>
        {
            ("term", dataSourceId),
        };

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var filterBytes = Encoding.UTF8.GetBytes(filter);

            var filterBase64 = Convert.ToBase64String(filterBytes);
            list.Add(("wrapper", filterBase64));
        }

        return list;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticsearchDataSourceContentManager"/> class.
    /// </summary>
    /// <param name="elasticClient">The elastic client.</param>
    /// <param name="logger">The logger.</param>
    public ElasticsearchDataSourceContentManager(
        ElasticsearchClient elasticClient,
        ILogger<ElasticsearchDataSourceContentManager> logger)
    {
        _elasticClient = elasticClient;
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
    /// <remarks>
    /// A cluster that cannot be reached reads here as an index holding nothing, exactly as it always has. A
    /// caller that has to tell those apart reads <see cref="TrySearchAsync"/> instead.
    /// </remarks>
    public async Task<IEnumerable<DataSourceSearchResult>> SearchAsync(
        IIndexProfileInfo indexProfile,
        float[] embedding,
        string dataSourceId,
        int topN,
        string filter = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TrySearchAsync(indexProfile, embedding, dataSourceId, topN, filter, cancellationToken);

        return outcome.Results;
    }

    /// <summary>
    /// Searches the index, reporting a query that could not run as a failure rather than as an index with
    /// nothing in it.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="embedding">The embedding.</param>
    /// <param name="dataSourceId">The data source id.</param>
    /// <param name="topN">The top n.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome of the search.</returns>
    public async Task<DataSourceSearchOutcome> TrySearchAsync(
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
            return DataSourceSearchOutcome.Success([]);
        }

        try
        {
            var mustQueries = new List<Action<QueryDescriptor<JsonObject>>>();

            foreach (var (kind, value) in BuildMustQueryDebug(dataSourceId, filter))
            {
                if (kind == "term")
                {
                    mustQueries.Add(m => m.Term(t => t
                        .Field(DataSourceConstants.ColumnNames.DataSourceId)
                        .Value(value)
                    ));
                }
                else if (kind == "wrapper")
                {
                    mustQueries.Add(m => m.Wrapper(w => w.Query(value)));
                }
            }

            var response = await _elasticClient.SearchAsync<JsonObject>(s => s
                .Indices(indexProfile.IndexFullName)
                .Knn(k => k
                .Field(DataSourceConstants.ColumnNames.Embedding)
                .QueryVector(embedding)
                .K(topN)
                .NumCandidates(topN * 10)
                    .Filter(f => f
                        .Bool(b => b
                        .Must(mustQueries.ToArray())
                    )
                )
            ).Size(topN)
            , cancellationToken);

            if (!response.IsValidResponse)
            {
                // An unreachable cluster and a refused credential both land here, and neither is a statement
                // about what the index holds.
                _logger.LogWarning("Elasticsearch data source vector search failed: {Error}", response.DebugInformation);

                return DataSourceSearchOutcome.Failure();
            }

            var results = new List<DataSourceSearchResult>();

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

                var referenceId = document.TryGetPropertyValue(DataSourceConstants.ColumnNames.ReferenceId, out var refNode)
                    ? refNode?.GetValue<string>()
                    : null;

                var title = document.TryGetPropertyValue(DataSourceConstants.ColumnNames.Title, out var titleNode)
                    ? titleNode?.GetValue<string>()
                    : null;

                var content = document.TryGetPropertyValue(DataSourceConstants.ColumnNames.Content, out var contentNode)
                    ? contentNode?.GetValue<string>()
                    : null;

                var chunkIndex = 0;

                if (document.TryGetPropertyValue(DataSourceConstants.ColumnNames.ChunkIndex, out var chunkIndexNode) && chunkIndexNode != null)
                {
                    chunkIndex = chunkIndexNode.GetValue<int>();
                }

                var referenceType = document.TryGetPropertyValue(DataSourceConstants.ColumnNames.ReferenceType, out var refTypeNode)
                    ? refTypeNode?.GetValue<string>()
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
                        Score = (float)(hit.Score ?? 0.0),
                        ContentType = ReadString(document, DataSourceConstants.ColumnNames.ContentType) ?? KnowledgeContentTypes.Text,
                        RootId = ReadString(document, DataSourceConstants.ColumnNames.RootId),
                        ParentId = ReadString(document, DataSourceConstants.ColumnNames.ParentId),
                        Page = ReadInt32(document, DataSourceConstants.ColumnNames.Page),
                        Filters = ReadFilters(document),
                    });
                }
            }

            return DataSourceSearchOutcome.Success(results
                .OrderByDescending(r => r.Score)
                .Take(topN)
                .ToList());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing data source vector search in Elasticsearch index '{IndexName}'", indexProfile.IndexFullName);

            return DataSourceSearchOutcome.Failure();
        }
    }

    /// <summary>
    /// Reads one string field, tolerating its absence.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or <see langword="null"/> when the field is not present.</returns>
    /// <remarks>
    /// An index built before typed knowledge existed simply has no such field, and a row from it reads as
    /// ordinary text rather than failing the search.
    /// </remarks>
    private static string ReadString(JsonObject document, string name)
    {
        if (!document.TryGetPropertyValue(name, out var node) || node == null)
        {
            return null;
        }

        try
        {
            return node.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads one integer field, tolerating its absence.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or <see langword="null"/> when the field is not present.</returns>
    private static int? ReadInt32(JsonObject document, string name)
    {
        if (!document.TryGetPropertyValue(name, out var node) || node == null)
        {
            return null;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the filter values stored alongside the row.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The filters, or <see langword="null"/> when the row carries none.</returns>
    private static Dictionary<string, object> ReadFilters(JsonObject document)
    {
        if (!document.TryGetPropertyValue(DataSourceConstants.ColumnNames.Filters, out var node) || node is not JsonObject filters)
        {
            return null;
        }

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in filters)
        {
            values[entry.Key] = entry.Value?.ToString();
        }

        return values;
    }

    /// <summary>
    /// Deletes every row belonging to the supplied reference identifiers in one request.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="referenceIds">The reference identifiers to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the delete ran.</returns>
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
            var values = referenceIds.Select(FieldValue (id) => id).ToArray();

            var response = await _elasticClient.DeleteByQueryAsync<JsonObject>(indexProfile.IndexFullName, d => d
                .Query(q => q
                    .Bool(b => b
                        .Must(
                            m => m.Term(t => t.Field(DataSourceConstants.ColumnNames.DataSourceId).Value(dataSourceId)),
                            m => m.Terms(t => t.Field(DataSourceConstants.ColumnNames.ReferenceId).Terms(new TermsQueryField(values)))
                        )
                    )
                ),
                cancellationToken);

            if (!response.IsValidResponse)
            {
                _logger.LogWarning("Elasticsearch delete by reference id failed for index '{IndexName}': {Error}", indexProfile.IndexFullName, response.DebugInformation);

                return false;
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
            _logger.LogWarning(ex, "Error deleting by reference id in Elasticsearch index '{IndexName}'.", indexProfile.IndexFullName);

            return false;
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
            var response = await _elasticClient.DeleteByQueryAsync<JsonObject>(indexProfile.IndexFullName, d => d
                .Query(q => q
                .Term(t => t
                    .Field(DataSourceConstants.ColumnNames.DataSourceId)
                    .Value(dataSourceId)
                )
            ),
            cancellationToken);

            if (!response.IsValidResponse)
            {
                _logger.LogWarning("Elasticsearch delete by data source ID failed for index '{IndexName}': {Error}",
                indexProfile.IndexFullName, response.DebugInformation);

                return 0;
            }

            return response.Deleted ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting documents by data source ID '{DataSourceId}' from Elasticsearch index '{IndexName}'.",
            dataSourceId, indexProfile.IndexFullName);

            return 0;
        }
    }
}
