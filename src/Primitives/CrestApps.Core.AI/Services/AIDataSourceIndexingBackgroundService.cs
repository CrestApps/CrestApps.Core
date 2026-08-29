using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

internal sealed class AIDataSourceIndexingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AIDataSourceIndexingBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDataSourceIndexingBackgroundService"/> class.
    /// </summary>
    /// <param name="queue">The queue.</param>
    /// <param name="serviceProvider">The scope factory.</param>
    /// <param name="logger">The logger.</param>
    public AIDataSourceIndexingBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<AIDataSourceIndexingBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Executes the operation.
    /// </summary>
    /// <param name="stoppingToken">The cancellation token.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queue = _serviceProvider.GetRequiredService<AIDataSourceIndexingQueue>();

        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Dequeued data-source work item {WorkItemType}. DataSourceId={DataSourceId}, SourceIndexProfileName={SourceIndexProfileName}, DocumentCount={DocumentCount}.", workItem.Type, workItem.DataSource?.ItemId, workItem.SourceIndexProfileName, workItem.DocumentIds.Count);
                }

                using var scope = _serviceProvider.CreateScope();
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
                    case AIDataSourceIndexingWorkItemType.SyncDataSourceDocuments:
                        await indexingService.SyncDataSourceDocumentsAsync(workItem.DataSourceId, workItem.DocumentIds, stoppingToken);
                        break;
                    case AIDataSourceIndexingWorkItemType.RemoveDataSourceDocuments:
                        await indexingService.RemoveDataSourceDocumentsAsync(workItem.DataSourceId, workItem.DocumentIds, stoppingToken);
                        break;
                }

                // External index writes (Elasticsearch, pgvector, …) commit immediately, but any writes made
                // to the transactional store during indexing — e.g. the Web data-source handler stamping
                // crawl state with the successful-index timestamp — only flush when the session is committed.
                // A request commits via StoreCommitterActionFilter; this background scope has no such filter,
                // so commit explicitly here. On failure the scope is disposed without committing, rolling those
                // writes back so the work is retried on the next pass.
                var committer = scope.ServiceProvider.GetService<IStoreCommitter>();

                if (committer is not null)
                {
                    await committer.CommitAsync(stoppingToken);
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
