namespace CrestApps.Core.AI.Documents.Ingestion;

/// <summary>
/// The per-run options an ingestion pipeline hands to every processor. Processors are singletons, so
/// anything that varies from one run to the next travels here rather than on the processor itself.
/// </summary>
public sealed class DocumentIngestionContext
{
    /// <summary>
    /// Gets the context used when a caller supplies none.
    /// </summary>
    public static DocumentIngestionContext Default { get; } = new();

    /// <summary>
    /// Gets how far figure enrichment is taken for this run.
    /// </summary>
    public FigureProcessingMode FigureMode { get; init; } = FigureProcessingMode.Auto;

    /// <summary>
    /// Gets the deployment used to describe figures. When <see langword="null"/> the vision slot is resolved.
    /// </summary>
    public string VisionDeploymentName { get; init; }

    /// <summary>
    /// Gets the deployment used for non-vision model work. When <see langword="null"/> the utility slot is
    /// resolved.
    /// </summary>
    public string UtilityDeploymentName { get; init; }

    /// <summary>
    /// Gets the ceiling on how many figures one document may have described.
    /// </summary>
    public int MaxFigureDescriptionsPerDocument { get; init; } = 25;

    /// <summary>
    /// Gets the BCP-47 language tag of the document, or <see langword="null"/> when it is unknown.
    /// </summary>
    public string Language { get; init; }

    /// <summary>
    /// Gets a value indicating whether figures are described during this run rather than backfilled after it.
    /// A chat upload describes inline because someone is waiting on the file; an indexer run does not,
    /// because vision latency must not gate how soon the text becomes searchable.
    /// </summary>
    public bool DescribeFiguresInline { get; init; } = true;

    /// <summary>
    /// Gets the data source the run feeds, or <see langword="null"/> for a chat upload.
    /// </summary>
    public string DataSourceId { get; init; }
}
