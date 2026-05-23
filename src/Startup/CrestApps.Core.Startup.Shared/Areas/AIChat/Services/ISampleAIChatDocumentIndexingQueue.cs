using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Startup.Shared.Areas.AIChat.Services;

public interface ISampleAIChatDocumentIndexingQueue
{
    ValueTask QueueIndexAsync(AIDocument document, IReadOnlyCollection<AIDocumentChunk> chunks, CancellationToken cancellationToken = default);

    ValueTask QueueDeleteChunksAsync(IReadOnlyCollection<string> chunkIds, CancellationToken cancellationToken = default);
}
