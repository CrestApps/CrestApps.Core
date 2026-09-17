using CrestApps.Core.AI.Documents.Ingestion;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// The ingestion settings one indexer applies to everything it reads.
/// </summary>
/// <remarks>
/// Figure transcription is the expensive part of ingestion, and how much of it is worth paying for depends
/// entirely on the corpus. A folder of scanned datasheets is worth describing every figure in; a folder of
/// meeting minutes is not. Settings live on the indexer rather than on the host so those two can sit side by
/// side.
/// </remarks>
public sealed class IndexerMetadata
{
    /// <summary>
    /// Gets or sets how far figure enrichment is taken.
    /// </summary>
    public FigureProcessingMode FigureMode { get; set; } = FigureProcessingMode.Auto;

    /// <summary>
    /// Gets or sets the deployment that transcribes figures, or <see langword="null"/> to use the host's
    /// vision deployment.
    /// </summary>
    /// <remarks>
    /// Validated to actually accept images. A deployment that cannot read a picture is not a cheaper choice,
    /// it is a setting that silently transcribes nothing.
    /// </remarks>
    public string VisionDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the ceiling on how many figures one document may have transcribed.
    /// </summary>
    public int MaxFigureDescriptionsPerDocument { get; set; } = 25;

    /// <summary>
    /// Gets or sets the deployment that answers the utility prompts ingestion runs, such as reading a
    /// document's front matter. <see langword="null"/> uses the host's utility deployment.
    /// </summary>
    public string UtilityDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the most items one run of this indexer may read, or <see langword="null"/> for the host
    /// default.
    /// </summary>
    public int? MaxItemsPerRun { get; set; }

    /// <summary>
    /// Gets or sets the BCP-47 language tag the corpus is written in, when it is known and uniform.
    /// </summary>
    public string Language { get; set; }
}
