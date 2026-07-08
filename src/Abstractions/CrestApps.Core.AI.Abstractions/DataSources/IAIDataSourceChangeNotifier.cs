namespace CrestApps.Core.AI.DataSources;

/// <summary>
/// Queues AI data source synchronization work when an external source changes.
/// </summary>
public interface IAIDataSourceChangeNotifier
{
    /// <summary>
    /// Queues a full synchronization for the specified AI data source.
    /// </summary>
    /// <param name="dataSourceId">The AI data source identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask QueueFullSyncAsync(string dataSourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an incremental add or update synchronization for the specified AI data source.
    /// </summary>
    /// <param name="dataSourceId">The AI data source identifier.</param>
    /// <param name="documentIds">The source document identifiers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask QueueDocumentsAddedOrUpdatedAsync(
        string dataSourceId,
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an incremental delete synchronization for the specified AI data source.
    /// </summary>
    /// <param name="dataSourceId">The AI data source identifier.</param>
    /// <param name="documentIds">The source document identifiers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask QueueDocumentsDeletedAsync(
        string dataSourceId,
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken = default);
}
