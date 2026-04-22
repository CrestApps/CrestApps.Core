using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

public interface IAIDataSourceIndexingQueue
{
    /// <summary>
    /// Queues a full synchronization of a mapped AI data source after its catalog record changes.
    /// Override this service when you need durable or distributed work dispatch instead of the default
    /// in-memory queue.
    /// </summary>
    /// <param name="dataSource">The mapped AI data source whose knowledge-base index should be rebuilt.</param>
    /// <param name="cancellationToken">A token that cancels queue submission.</param>
    /// <returns>A value task that completes when the work item has been accepted by the queue implementation.</returns>
    ValueTask QueueSyncDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues cleanup of a mapped AI data source after its catalog record is deleted.
    /// </summary>
    /// <param name="dataSource">The mapped AI data source whose indexed knowledge-base documents should be removed.</param>
    /// <param name="cancellationToken">A token that cancels queue submission.</param>
    /// <returns>A value task that completes when the work item has been accepted by the queue implementation.</returns>
    ValueTask QueueDeleteDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues targeted synchronization of source document changes for data sources mapped to the specified source index profile.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name that produced the document mutations.</param>
    /// <param name="documentIds">The source document ids that should be synchronized into matching knowledge-base indexes.</param>
    /// <param name="cancellationToken">A token that cancels queue submission.</param>
    /// <returns>A value task that completes when the work item has been accepted by the queue implementation.</returns>
    ValueTask QueueSyncSourceDocumentsAsync(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues targeted removal of source document changes for data sources mapped to the specified source index profile.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name that produced the delete operation.</param>
    /// <param name="documentIds">The source document ids that should be removed from matching knowledge-base indexes.</param>
    /// <param name="cancellationToken">A token that cancels queue submission.</param>
    /// <returns>A value task that completes when the work item has been accepted by the queue implementation.</returns>
    ValueTask QueueRemoveSourceDocumentsAsync(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default);
}
