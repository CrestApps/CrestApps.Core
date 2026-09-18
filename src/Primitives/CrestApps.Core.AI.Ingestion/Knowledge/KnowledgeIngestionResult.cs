namespace CrestApps.Core.AI.Ingestion.Knowledge;

/// <summary>
/// What one ingest produced.
/// </summary>
public sealed class KnowledgeIngestionResult
{
    /// <summary>
    /// Gets or sets the identifier of the ingested document, which every object it produced hangs off.
    /// </summary>
    public string RootId { get; set; }

    /// <summary>
    /// Gets or sets how many objects were stored.
    /// </summary>
    public int ObjectCount { get; set; }

    /// <summary>
    /// Gets or sets how many figures were stored.
    /// </summary>
    public int FigureCount { get; set; }

    /// <summary>
    /// Gets or sets how many figures are still waiting to be transcribed.
    /// </summary>
    public int PendingDescriptionCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the ingest succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets why the ingest failed, when it did.
    /// </summary>
    public string Error { get; set; }
}
