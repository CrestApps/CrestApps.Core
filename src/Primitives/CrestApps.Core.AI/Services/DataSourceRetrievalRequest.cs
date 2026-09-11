using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// The query-time parameters used to search a single AI data source knowledge base.
/// </summary>
internal sealed class DataSourceRetrievalRequest
{
    /// <summary>
    /// Gets the identifier of the data source to search.
    /// </summary>
    public string DataSourceId { get; init; }

    /// <summary>
    /// Gets the natural-language phrases to embed and search for. Every phrase is embedded in one batched
    /// call and searched independently, and the result sets are fused into a single ranking.
    /// </summary>
    public IReadOnlyList<string> Queries { get; init; }

    /// <summary>
    /// Gets the number of top-scoring results to return. When not set, the configured site default is used.
    /// </summary>
    public int? TopNDocuments { get; init; }

    /// <summary>
    /// Gets the strictness threshold used to derive the minimum score a result must reach.
    /// </summary>
    public int? Strictness { get; init; }

    /// <summary>
    /// Gets the OData filter expression translated to the provider's own filter syntax before searching.
    /// </summary>
    public string Filter { get; init; }

    /// <summary>
    /// Gets the retrieval mode that decides whether matching chunks or their full source documents are returned.
    /// </summary>
    public DataSourceRetrievalMode RetrievalMode { get; init; }

    /// <summary>
    /// Gets a value indicating whether the model must answer only from the retrieved content. This only
    /// changes the guidance returned when nothing relevant is found.
    /// </summary>
    public bool IsInScope { get; init; }
}
