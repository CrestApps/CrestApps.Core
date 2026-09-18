namespace CrestApps.Core.AI.Ingestion.Knowledge;

/// <summary>
/// The per-run options an ingest takes.
/// </summary>
public sealed class KnowledgeIngestionOptions
{
    /// <summary>
    /// Gets or sets how far figure enrichment is taken.
    /// </summary>
    public FigureProcessingMode FigureMode { get; set; } = FigureProcessingMode.Auto;

    /// <summary>
    /// Gets or sets the deployment used to transcribe figures, or <see langword="null"/> to resolve the slot.
    /// </summary>
    public string VisionDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the ceiling on how many figures one document may have transcribed.
    /// </summary>
    public int MaxFigureDescriptionsPerDocument { get; set; } = 25;

    /// <summary>
    /// Gets or sets the BCP-47 language tag of the document, when it is known.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets the words that mark a figure as a chart, or <see langword="null"/> to use
    /// <see cref="KnowledgeObjectBuilder.DefaultChartKeywords"/>.
    /// </summary>
    /// <remarks>
    /// The defaults cover the common European spellings. A corpus in a language they do not cover replaces
    /// them here rather than having every chart stored as an untyped figure.
    /// </remarks>
    public IReadOnlyList<string> ChartKeywords { get; set; }

    /// <summary>
    /// Gets or sets the indexer that produced the file, or <see langword="null"/> for a manual upload.
    /// </summary>
    public string IndexerId { get; set; }

    /// <summary>
    /// Gets or sets the identifier the connector knows the source item by, or <see langword="null"/> for a
    /// manual upload.
    /// </summary>
    public string SourceItemId { get; set; }

    /// <summary>
    /// Gets or sets how often the backfill service looks for figures still owed a transcription, in seconds.
    /// </summary>
    /// <remarks>
    /// A per-run ingest never reads this. It is configured once, through
    /// <c>services.Configure&lt;KnowledgeIngestionOptions&gt;(...)</c>, and read by the hosted backfill job.
    /// </remarks>
    public int BackfillIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets how many figures one pass of the backfill takes from one data source.
    /// </summary>
    public int BackfillBatchSize { get; set; } = 20;

    /// <summary>
    /// Gets or sets how many figures the backfill transcribes at once within one data source.
    /// </summary>
    public int MaxConcurrentVisionCalls { get; set; } = 2;
}
