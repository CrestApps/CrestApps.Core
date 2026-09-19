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
