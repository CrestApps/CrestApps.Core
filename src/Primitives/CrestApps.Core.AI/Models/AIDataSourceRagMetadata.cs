namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents query-time RAG (Retrieval-Augmented Generation) parameters for data sources.
/// This metadata can be attached to AIProfile or ChatInteraction to customize
/// RAG behavior for any provider.
/// </summary>
public sealed class AIDataSourceRagMetadata
{
    /// <summary>
    /// Gets or sets the strictness threshold for categorizing documents as relevant.
    /// Values range from 1 to 5, with higher values meaning a higher threshold for relevance.
    /// </summary>
    public int? Strictness { get; set; }

    /// <summary>
    /// Gets or sets the number of top-scoring documents to retrieve from the data index.
    /// Values range from 3 to 20.
    /// </summary>
    public int? TopNDocuments { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to limit retrieval to in-scope documents only.
    /// When <see langword="false"/> (the default), the model may supplement retrieved data with general knowledge.
    /// </summary>
    public bool IsInScope { get; set; }

    /// <summary>
    /// Gets or sets the filter expression to query a subset of indexed data.
    /// The filter format depends on the search provider (e.g., Elasticsearch query for Elasticsearch).
    /// </summary>
    public string Filter { get; set; }

    /// <summary>
    /// Gets or sets the kinds of knowledge to retrieve, for example only figures and charts. When empty,
    /// every kind is retrieved. See <see cref="CrestApps.Core.Infrastructure.Indexing.KnowledgeObjectTypes"/>.
    /// </summary>
    /// <remarks>
    /// A tool instance could already be pinned to particular kinds through its own settings, but a data
    /// source attached straight to a profile had no equivalent: the same knowledge base narrowed one way when
    /// the model chose to search it and not at all when the profile searched it preemptively. This is that
    /// restriction for the preemptive path.
    /// <para>
    /// Asking for text also admits rows written before typed knowledge existed, which carry no kind at all.
    /// </para>
    /// </remarks>
    public string[] ObjectTypes { get; set; }
}
