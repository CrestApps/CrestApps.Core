using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Handlers;

internal sealed class AIDataSourceCatalogIndexingHandler : CatalogEntryHandlerBase<AIDataSource>
{
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly ILogger<AIDataSourceCatalogIndexingHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDataSourceCatalogIndexingHandler"/> class.
    /// </summary>
    /// <param name="indexingQueue">The indexing queue.</param>
    /// <param name="logger">The logger.</param>
    public AIDataSourceCatalogIndexingHandler(
        IAIDataSourceIndexingQueue indexingQueue,
        ILogger<AIDataSourceCatalogIndexingHandler> logger)
    {
        _indexingQueue = indexingQueue;
        _logger = logger;
    }

    /// <summary>
    /// Createds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task CreatedAsync(CreatedContext<AIDataSource> context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("AI data source catalog event '{EventName}' queued full synchronization for data source '{DataSourceId}'.", nameof(CreatedAsync), context.Model.ItemId);
            }

            await _indexingQueue.QueueSyncDataSourceAsync(context.Model, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue initial indexing for data source '{DataSourceId}'.", context.Model.ItemId);
        }
    }

    /// <summary>
    /// Updateds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task UpdatedAsync(UpdatedContext<AIDataSource> context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("AI data source catalog event '{EventName}' queued full synchronization for data source '{DataSourceId}'.", nameof(UpdatedAsync), context.Model.ItemId);
            }

            await _indexingQueue.QueueSyncDataSourceAsync(context.Model, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue synchronization for updated data source '{DataSourceId}'.", context.Model.ItemId);
        }
    }

    /// <summary>
    /// Deleteds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task DeletedAsync(DeletedContext<AIDataSource> context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("AI data source catalog event '{EventName}' queued cleanup for data source '{DataSourceId}'.", nameof(DeletedAsync), context.Model.ItemId);
            }

            await _indexingQueue.QueueDeleteDataSourceAsync(context.Model, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue cleanup for deleted data source '{DataSourceId}'.", context.Model.ItemId);
        }
    }
}
