using CrestApps.Core.AI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Memory;

/// <summary>
/// Drains <see cref="AIMemoryIndexingQueue"/> and writes each memory to the configured AI Memory index.
/// </summary>
internal sealed class AIMemoryIndexingBackgroundService : BackgroundService
{
    private readonly AIMemoryIndexingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AIMemoryIndexingBackgroundService> _logger;

    public AIMemoryIndexingBackgroundService(
        AIMemoryIndexingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AIMemoryIndexingBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.ReadAllAsync(stoppingToken))
        {
            await ProcessAsync(workItem, stoppingToken);
        }
    }

    internal async Task ProcessAsync(AIMemoryIndexingWorkItem workItem, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var indexingService = scope.ServiceProvider.GetRequiredService<AIMemoryIndexingService>();

            if (workItem.Memory is not null)
            {
                await indexingService.IndexAsync(workItem.Memory, cancellationToken);
            }
            else
            {
                await indexingService.DeleteAsync([workItem.DeletedMemoryId], cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The memory itself is saved; only its index entry is missing, and search falls back to the store.
            _logger.LogError(
                ex,
                "Failed to {Operation} AI memory '{MemoryId}' in the configured memory index.",
                workItem.Memory is not null ? "index" : "remove",
                workItem.Memory?.ItemId ?? workItem.DeletedMemoryId);
        }
    }
}
