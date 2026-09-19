namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// How one source run ended.
/// </summary>
public enum FileSourceRunStatus
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
