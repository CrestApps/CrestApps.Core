using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Queues synchronization work for externally managed AI data source sources.
/// </summary>
public sealed class DefaultAIDataSourceChangeNotifier : IAIDataSourceChangeNotifier
{
    private readonly IAIDataSourceStore _dataSourceCatalog;
    private readonly IAIDataSourceIndexingQueue _indexingQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIDataSourceChangeNotifier"/> class.
    /// </summary>
    /// <param name="dataSourceCatalog">The data source catalog.</param>
    /// <param name="indexingQueue">The indexing queue.</param>
    public DefaultAIDataSourceChangeNotifier(
        IAIDataSourceStore dataSourceCatalog,
        IAIDataSourceIndexingQueue indexingQueue)
    {
        _dataSourceCatalog = dataSourceCatalog;
        _indexingQueue = indexingQueue;
    }

    /// <summary>
    /// Queues full sync.
    /// </summary>
    /// <param name="dataSourceId">The data source id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask QueueFullSyncAsync(string dataSourceId, CancellationToken cancellationToken = default)
    {
        var dataSource = await GetRequiredDataSourceAsync(dataSourceId, cancellationToken);

        await _indexingQueue.QueueSyncDataSourceAsync(dataSource, cancellationToken);
    }

    /// <summary>
    /// Queues documents added or updated.
    /// </summary>
    /// <param name="dataSourceId">The data source id.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask QueueDocumentsAddedOrUpdatedAsync(string dataSourceId, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        _ = await GetRequiredDataSourceAsync(dataSourceId, cancellationToken);

        await _indexingQueue.QueueSyncDataSourceDocumentsAsync(dataSourceId, documentIds, cancellationToken);
    }

    /// <summary>
    /// Queues documents deleted.
    /// </summary>
    /// <param name="dataSourceId">The data source id.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask QueueDocumentsDeletedAsync(string dataSourceId, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        _ = await GetRequiredDataSourceAsync(dataSourceId, cancellationToken);

        await _indexingQueue.QueueRemoveDataSourceDocumentsAsync(dataSourceId, documentIds, cancellationToken);
    }

    private async Task<AIDataSource> GetRequiredDataSourceAsync(string dataSourceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);

        var dataSource = await _dataSourceCatalog.FindByIdAsync(dataSourceId, cancellationToken);

        return dataSource ?? throw new InvalidOperationException($"AI data source '{dataSourceId}' was not found.");
    }
}
