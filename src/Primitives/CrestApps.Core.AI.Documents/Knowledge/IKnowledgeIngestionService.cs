using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Knowledge;

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

/// <summary>
/// Turns a file into the typed knowledge objects of one AI data source. Manual uploads and indexers both go
/// through here, so both produce exactly the same knowledge.
/// </summary>
public interface IKnowledgeIngestionService
{
    /// <summary>
    /// Ingests one file.
    /// </summary>
    /// <param name="dataSource">The data source to ingest into.</param>
    /// <param name="content">The file content.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="options">The per-run options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the ingest produced.</returns>
    Task<KnowledgeIngestionResult> IngestAsync(
        AIDataSource dataSource,
        Stream content,
        string fileName,
        string mediaType,
        KnowledgeIngestionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an ingested document and everything it produced.
    /// </summary>
    /// <param name="dataSource">The data source the document belongs to.</param>
    /// <param name="rootId">The document's canonical identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RemoveAsync(AIDataSource dataSource, string rootId, CancellationToken cancellationToken = default);
}
