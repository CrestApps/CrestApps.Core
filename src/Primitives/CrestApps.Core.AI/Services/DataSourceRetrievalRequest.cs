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
    /// Gets how an empty result should be reported to the model, when the caller is in a position to say.
    /// <see langword="true"/> states that the answer is not available; <see langword="false"/> invites a
    /// fall back to general knowledge; <see langword="null"/> — the default — reports only that nothing was
    /// found.
    /// </summary>
    /// <remarks>
    /// Only a caller that owns the model's whole turn can hold the model to an answering policy, because the
    /// policy has to reach the system prompt. A single tool cannot: the model is free to ignore a sentence in
    /// one tool result. So a caller configured per profile or interaction passes its setting through, while a
    /// standalone tool leaves this unset and states the plain fact instead of implying a constraint it has no
    /// way to enforce.
    /// </remarks>
    public bool? IsInScope { get; init; }
}
