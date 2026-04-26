using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

internal sealed class AIDataSourceIndexingBackgroundService : BackgroundService
{
    private readonly AIDataSourceIndexingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AIDataSourceIndexingBackgroundService> _logger;

    public AIDataSourceIndexingBackgroundService(
        AIDataSourceIndexingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AIDataSourceIndexingBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Dequeued data-source work item {WorkItemType}. DataSourceId={DataSourceId}, SourceIndexProfileName={SourceIndexProfileName}, DocumentCount={DocumentCount}.", workItem.Type, workItem.DataSource?.ItemId, workItem.SourceIndexProfileName, workItem.DocumentIds.Count);
                }

                await using var scope = _scopeFactory.CreateAsyncScope();
                var indexingService = scope.ServiceProvider.GetRequiredService<IAIDataSourceIndexingService>();

                switch (workItem.Type)
                {
                    case AIDataSourceIndexingWorkItemType.SyncDataSource:
                        await indexingService.SyncDataSourceAsync(workItem.DataSource, stoppingToken);
                        break;
                    case AIDataSourceIndexingWorkItemType.DeleteDataSource:
                        await indexingService.DeleteDataSourceDocumentsAsync(workItem.DataSource, stoppingToken);
                        break;
                    case AIDataSourceIndexingWorkItemType.SyncSourceDocuments:
                        await indexingService.SyncSourceDocumentsAsync(workItem.SourceIndexProfileName, workItem.DocumentIds, stoppingToken);
                        break;
                    case AIDataSourceIndexingWorkItemType.RemoveSourceDocuments:
                        await indexingService.RemoveSourceDocumentsAsync(workItem.SourceIndexProfileName, workItem.DocumentIds, stoppingToken);
                        break;
                }

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Completed data-source work item {WorkItemType}. DataSourceId={DataSourceId}, SourceIndexProfileName={SourceIndexProfileName}.", workItem.Type, workItem.DataSource?.ItemId, workItem.SourceIndexProfileName);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing queued data-source indexing work.");
            }
        }
    }
}
