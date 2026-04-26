using System.Threading.Channels;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.Services;

public sealed class SampleAIChatDocumentIndexingQueue : ISampleAIChatDocumentIndexingQueue
{
    private readonly Channel<SampleAIChatDocumentIndexingWorkItem> _channel = Channel.CreateUnbounded<SampleAIChatDocumentIndexingWorkItem>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, });
    public ValueTask QueueIndexAsync(AIDocument document, IReadOnlyCollection<AIDocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);

        return _channel.Writer.WriteAsync(SampleAIChatDocumentIndexingWorkItem.ForIndex(document, chunks.ToArray()), cancellationToken);
    }

    public ValueTask QueueDeleteChunksAsync(IReadOnlyCollection<string> chunkIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);

        return _channel.Writer.WriteAsync(SampleAIChatDocumentIndexingWorkItem.ForDeleteChunks(chunkIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray()), cancellationToken);
    }

    internal IAsyncEnumerable<SampleAIChatDocumentIndexingWorkItem> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}

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

internal enum SampleAIChatDocumentIndexingWorkItemType
{
    Index,
    DeleteChunks,
}
