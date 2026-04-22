using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

public interface IAIDataSourceIndexingService
{
    /// <summary>
    /// Reconciles every configured AI data source by reading each mapping and synchronizing its knowledge-base index.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the synchronization operation.</param>
    /// <returns>A task that completes when all configured data sources have been processed.</returns>
    Task SyncAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a full synchronization for one AI data source mapping.
    /// </summary>
    /// <param name="dataSource">The AI data source definition to rebuild.</param>
    /// <param name="cancellationToken">A token that cancels the synchronization operation.</param>
    /// <returns>A task that completes when the mapped knowledge-base index has been rebuilt.</returns>
    Task SyncDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes a set of source documents across all data sources that map to any matching source profile.
    /// </summary>
    /// <param name="documentIds">The source document ids that should be synchronized.</param>
    /// <param name="cancellationToken">A token that cancels the synchronization operation.</param>
    /// <returns>A task that completes when the matching source documents have been processed.</returns>
    Task SyncSourceDocumentsAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes a set of source documents for data sources mapped to one source index profile.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name that owns the changed documents.</param>
    /// <param name="documentIds">The source document ids that should be synchronized.</param>
    /// <param name="cancellationToken">A token that cancels the synchronization operation.</param>
    /// <returns>A task that completes when the matching source documents have been processed.</returns>
    Task SyncSourceDocumentsAsync(string sourceIndexProfileName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a set of source documents from all data sources that map to any matching source profile.
    /// </summary>
    /// <param name="documentIds">The source document ids that should be removed.</param>
    /// <param name="cancellationToken">A token that cancels the removal operation.</param>
    /// <returns>A task that completes when the matching source documents have been removed.</returns>
    Task RemoveSourceDocumentsAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a set of source documents for data sources mapped to one source index profile.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name that owns the deleted documents.</param>
    /// <param name="documentIds">The source document ids that should be removed.</param>
    /// <param name="cancellationToken">A token that cancels the removal operation.</param>
    /// <returns>A task that completes when the matching source documents have been removed.</returns>
    Task RemoveSourceDocumentsAsync(string sourceIndexProfileName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all indexed documents for a mapped AI data source from its knowledge-base index.
    /// </summary>
    /// <param name="dataSource">The AI data source definition whose indexed documents should be removed.</param>
    /// <param name="cancellationToken">A token that cancels the delete operation.</param>
    /// <returns>A task that completes when the data-source documents have been removed.</returns>
    Task DeleteDataSourceDocumentsAsync(AIDataSource dataSource, CancellationToken cancellationToken = default);
}
