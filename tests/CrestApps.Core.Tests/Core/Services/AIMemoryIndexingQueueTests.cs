using CrestApps.Core.AI;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Builders;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIMemoryIndexingQueueTests
{
    [Fact]
    public async Task CreatedAsync_QueuesASnapshotForIndexing()
    {
        var queue = new AIMemoryIndexingQueue();
        var handler = new AIMemoryEntryHandler(new HttpContextAccessor(), TimeProvider.System, queue);
        var memory = new AIMemoryEntry
        {
            ItemId = "memory-1",
            UserId = "user-1",
            Name = "preferred_name",
            Description = "The user's preferred display name.",
            Content = "The user prefers to be called Mike.",
        };

        await handler.CreatedAsync(new CreatedContext<AIMemoryEntry>(memory), TestContext.Current.CancellationToken);

        // A later edit to the caller's instance must not change what the worker indexes.
        memory.Content = "changed";

        var workItem = await ReadOneAsync(queue);
        Assert.Equal("memory-1", workItem.Memory.ItemId);
        Assert.Equal("The user prefers to be called Mike.", workItem.Memory.Content);
        Assert.Null(workItem.DeletedMemoryId);
    }

    [Fact]
    public async Task DeletedAsync_QueuesTheMemoryIdForRemoval()
    {
        var queue = new AIMemoryIndexingQueue();
        var handler = new AIMemoryEntryHandler(new HttpContextAccessor(), TimeProvider.System, queue);

        await handler.DeletedAsync(new DeletedContext<AIMemoryEntry>(new AIMemoryEntry { ItemId = "memory-1" }), TestContext.Current.CancellationToken);

        var workItem = await ReadOneAsync(queue);
        Assert.Null(workItem.Memory);
        Assert.Equal("memory-1", workItem.DeletedMemoryId);
    }

    [Fact]
    public void AddAIMemory_RegistersTheIndexingHandlerAndWorker()
    {
        var services = new ServiceCollection();

        new CrestAppsAISuiteBuilder(services).AddAIMemory();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ICatalogEntryHandler<AIMemoryEntry>) && descriptor.ImplementationType == typeof(AIMemoryEntryHandler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(AIMemoryIndexingBackgroundService));
    }

    [Fact]
    public void AddCoreAIMemory_LeavesIndexingToTheHost()
    {
        // Hosts that index memories with their own catalog handler (the Orchard Core module does, through a
        // deferred task) call AddCoreAIMemory alone; registering the framework handler too would index twice.
        var services = new ServiceCollection();

        services.AddCoreAIMemory();

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ICatalogEntryHandler<AIMemoryEntry>));
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(AIMemoryIndexingBackgroundService));
    }

    private static async Task<AIMemoryIndexingWorkItem> ReadOneAsync(AIMemoryIndexingQueue queue)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var workItem in queue.ReadAllAsync(timeout.Token))
        {
            return workItem;
        }

        throw new InvalidOperationException("No work item was queued.");
    }
}
