using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.Startup.Shared.Areas.AIChat.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Startup.Shared.Areas.AIChat.BackgroundServices;

public sealed class AIChatDocumentIndexingBackgroundService : BackgroundService
{
    private readonly SampleAIChatDocumentIndexingQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AIChatDocumentIndexingBackgroundService> _logger;

    public AIChatDocumentIndexingBackgroundService(
        SampleAIChatDocumentIndexingQueue queue,
        IServiceProvider serviceProvider,
        ILogger<AIChatDocumentIndexingBackgroundService> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                var indexingService = _serviceProvider.GetRequiredService<DefaultAIDocumentIndexingService>();

                switch (workItem.Type)
                {
                    case SampleAIChatDocumentIndexingWorkItemType.Index:
                        await indexingService.IndexAsync(workItem.Document, workItem.Chunks, stoppingToken);
                        break;
                    case SampleAIChatDocumentIndexingWorkItemType.DeleteChunks:
                        await indexingService.DeleteChunksAsync(workItem.ChunkIds, stoppingToken);
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing queued chat document indexing work.");
            }
        }
    }
}
