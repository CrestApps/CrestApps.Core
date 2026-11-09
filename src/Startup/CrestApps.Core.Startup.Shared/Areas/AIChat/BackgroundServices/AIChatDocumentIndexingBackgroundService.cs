using System.Diagnostics;
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
            CrestAppsTelemetry.QueueDepth.Add(-1, SampleAIChatDocumentIndexingQueue.QueueTag);
            CrestAppsTelemetry.QueueWaitDuration.Record(Stopwatch.GetElapsedTime(workItem.QueuedTimestamp).TotalSeconds, SampleAIChatDocumentIndexingQueue.QueueTag);

            var workItemType = workItem.Type == SampleAIChatDocumentIndexingWorkItemType.Index
                ? "index"
                : "delete_chunks";

            using var activity = CrestAppsTelemetry.StartLinkedActivity("crestapps.background.item", workItem.QueuedContext);
            activity?.SetTag(CrestAppsTelemetry.ServiceTag, nameof(AIChatDocumentIndexingBackgroundService));
            activity?.SetTag(CrestAppsTelemetry.WorkItemTypeTag, workItemType);

            using var logScope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["WorkItemType"] = workItemType,
                ["DocumentId"] = workItem.Document?.ItemId,
            });

            var startTimestamp = Stopwatch.GetTimestamp();
            var outcome = CrestAppsTelemetry.OutcomeOk;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var indexingService = scope.ServiceProvider.GetRequiredService<DefaultAIDocumentIndexingService>();

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
                outcome = CrestAppsTelemetry.OutcomeCancelled;

                break;
            }
            catch (Exception ex)
            {
                outcome = CrestAppsTelemetry.OutcomeError;
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message).AddException(ex);

                _logger.LogError(ex, "An error occurred while processing queued chat document indexing work.");
            }
            finally
            {
                CrestAppsTelemetry.BackgroundItemDuration.Record(
                    Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                    new KeyValuePair<string, object>(CrestAppsTelemetry.ServiceTag, nameof(AIChatDocumentIndexingBackgroundService)),
                    new KeyValuePair<string, object>(CrestAppsTelemetry.WorkItemTypeTag, workItemType),
                    new KeyValuePair<string, object>(CrestAppsTelemetry.OutcomeTag, outcome));
            }
        }
    }
}
