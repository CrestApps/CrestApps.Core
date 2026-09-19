using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Startup.Shared.Areas.AIChat.Services;

internal sealed class SampleAIChatDocumentIndexingWorkItem
{
    public AIDocument Document { get; private init; }
    public IReadOnlyCollection<AIDocumentChunk> Chunks { get; private init; } = [];
    public IReadOnlyCollection<string> ChunkIds { get; private init; } = [];
    public SampleAIChatDocumentIndexingWorkItemType Type { get; private init; }

    public static SampleAIChatDocumentIndexingWorkItem ForIndex(AIDocument document, IReadOnlyCollection<AIDocumentChunk> chunks)
    {
        return new()
        {
            Document = document,
            Chunks = chunks,
            Type = SampleAIChatDocumentIndexingWorkItemType.Index,
        };
    }

    public static SampleAIChatDocumentIndexingWorkItem ForDeleteChunks(IReadOnlyCollection<string> chunkIds)
    {
        return new()
        {
            ChunkIds = chunkIds,
            Type = SampleAIChatDocumentIndexingWorkItemType.DeleteChunks,
        };
    }
}
