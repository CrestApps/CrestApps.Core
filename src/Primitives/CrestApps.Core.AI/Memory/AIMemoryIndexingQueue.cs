using System.Threading.Channels;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Memory;

/// <summary>
/// Hands memory index writes to <see cref="AIMemoryIndexingBackgroundService"/>, so saving a memory never waits
/// on an embedding call and a failed embedding never fails the save.
/// </summary>
internal sealed class AIMemoryIndexingQueue
{
    private readonly Channel<AIMemoryIndexingWorkItem> _channel = Channel.CreateUnbounded<AIMemoryIndexingWorkItem>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public ValueTask QueueIndexAsync(AIMemoryEntry memory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        // A snapshot: the caller's instance may change again before the worker reads it.
        var snapshot = new AIMemoryEntry
        {
            ItemId = memory.ItemId,
            UserId = memory.UserId,
            Name = memory.Name,
            Description = memory.Description,
            Content = memory.Content,
            CreatedUtc = memory.CreatedUtc,
            UpdatedUtc = memory.UpdatedUtc,
        };

        return _channel.Writer.WriteAsync(new AIMemoryIndexingWorkItem(snapshot, null), cancellationToken);
    }

    public ValueTask QueueDeleteAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);

        return _channel.Writer.WriteAsync(new AIMemoryIndexingWorkItem(null, memoryId), cancellationToken);
    }

    internal IAsyncEnumerable<AIMemoryIndexingWorkItem> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}

/// <summary>
/// One memory index write: <see cref="Memory"/> to index, or <see cref="DeletedMemoryId"/> to remove.
/// </summary>
internal sealed record AIMemoryIndexingWorkItem(AIMemoryEntry Memory, string DeletedMemoryId);
