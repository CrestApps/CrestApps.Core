namespace CrestApps.Core.AI.Indexers;

/// <summary>
/// How one indexer run ended.
/// </summary>
public enum IndexerRunStatus
{
    /// <summary>
    /// The run has never happened.
    /// </summary>
    None = 0,

    /// <summary>
    /// The run is happening now.
    /// </summary>
    Running = 1,

    /// <summary>
    /// The run listed the whole source and everything it tried succeeded.
    /// </summary>
    Succeeded = 2,

    /// <summary>
    /// The run did some of what it set out to do: some items failed, or the listing was only part of the
    /// source.
    /// </summary>
    PartiallyCompleted = 3,

    /// <summary>
    /// The run could not start or its discovery failed. Nothing was ingested and nothing was removed.
    /// </summary>
    Failed = 4,
}

/// <summary>
/// What one indexer run did. Stored on the indexer record, so the last run is visible without a log.
/// </summary>
/// <remarks>
/// An indexer runs unattended, so this is the only account anyone gets of what it did. In particular it
/// records whether the listing was complete, because that is the difference between "these files are gone"
/// and "I could not see them this time".
/// </remarks>
public sealed class IndexerRunSummary
{
    /// <summary>
    /// Gets or sets when the run started.
    /// </summary>
    public DateTime StartedUtc { get; set; }

    /// <summary>
    /// Gets or sets when the run finished, or <see langword="null"/> while it is still running.
    /// </summary>
    public DateTime? CompletedUtc { get; set; }

    /// <summary>
    /// Gets or sets how the run ended.
    /// </summary>
    public IndexerRunStatus Status { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the listing was the whole of what the source holds.
    /// </summary>
    /// <remarks>
    /// Removals happen only when this is <see langword="true"/>. A partial listing taken for a complete one
    /// deletes everything it failed to see, which is the worst thing this subsystem can do.
    /// </remarks>
    public bool DiscoveryCompleted { get; set; }

    /// <summary>
    /// Gets or sets where the next run resumes, when this one saw only a window onto the source.
    /// </summary>
    public string DiscoveryCursor { get; set; }

    /// <summary>
    /// Gets or sets how many items the source reported.
    /// </summary>
    public int ItemsDiscovered { get; set; }

    /// <summary>
    /// Gets or sets how many items were read because they were new or had changed.
    /// </summary>
    public int ItemsIndexed { get; set; }

    /// <summary>
    /// Gets or sets how many items were left alone because nothing about them had changed.
    /// </summary>
    public int ItemsSkipped { get; set; }

    /// <summary>
    /// Gets or sets how many items were removed because the source no longer holds them.
    /// </summary>
    public int ItemsDeleted { get; set; }

    /// <summary>
    /// Gets or sets how many items failed.
    /// </summary>
    public int ItemsFailed { get; set; }

    /// <summary>
    /// Gets or sets how many figures the run stored.
    /// </summary>
    public int FiguresDiscovered { get; set; }

    /// <summary>
    /// Gets or sets how many of those figures are still waiting to be transcribed.
    /// </summary>
    /// <remarks>
    /// A run makes no vision calls of its own: ingestion never waits for a model, so transcription is the
    /// backfill service's work. What a run can say is how much of it there is to do.
    /// </remarks>
    public int FiguresPendingDescription { get; set; }

    /// <summary>
    /// Gets or sets what went wrong, or why the listing was incomplete.
    /// </summary>
    public string Error { get; set; }
}
