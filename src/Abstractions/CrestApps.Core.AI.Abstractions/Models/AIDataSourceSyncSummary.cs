namespace CrestApps.Core.AI.Models;

/// <summary>
/// How the last synchronization of a data source into its knowledge-base index ended.
/// </summary>
public enum AIDataSourceSyncStatus
{
    /// <summary>
    /// The data source has never been synchronized.
    /// </summary>
    None = 0,

    /// <summary>
    /// The sync read the source through to the end and everything it read reached the index.
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// The sync could not finish. Whatever it did not write is not searchable.
    /// </summary>
    Failed = 2,
}

/// <summary>
/// What the last synchronization of a data source did. Stored on the data source record, so a sync that
/// indexed nothing is visible without reading a log.
/// </summary>
/// <remarks>
/// Synchronization runs on a background queue, so no one is watching when it fails. Without this record an
/// index write that failed — a stopped vector database, a provider that refused to create the index — leaves
/// a data source that reads as perfectly normal and answers every question with "not found", because the
/// knowledge objects were stored and none of them reached the index.
/// </remarks>
public sealed class AIDataSourceSyncSummary
{
    /// <summary>
    /// Gets or sets when the sync started.
    /// </summary>
    public DateTime StartedUtc { get; set; }

    /// <summary>
    /// Gets or sets when the sync finished.
    /// </summary>
    public DateTime CompletedUtc { get; set; }

    /// <summary>
    /// Gets or sets how the sync ended.
    /// </summary>
    public AIDataSourceSyncStatus Status { get; set; }

    /// <summary>
    /// Gets or sets how many index documents the sync wrote.
    /// </summary>
    /// <remarks>
    /// One per chunk rather than one per source document, because a chunk is the row the index actually
    /// holds. A failed sync keeps the count it had reached, so a partial write reads as the partial write it
    /// was rather than as nothing at all.
    /// </remarks>
    public int DocumentsIndexed { get; set; }

    /// <summary>
    /// Gets or sets why the sync failed, or <see langword="null"/> when it succeeded.
    /// </summary>
    public string Error { get; set; }
}
