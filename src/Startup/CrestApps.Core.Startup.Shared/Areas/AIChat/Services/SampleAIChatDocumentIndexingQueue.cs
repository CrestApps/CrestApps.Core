using System.Threading.Channels;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Startup.Shared.Areas.AIChat.Services;

public sealed class SampleAIChatDocumentIndexingQueue : ISampleAIChatDocumentIndexingQueue
{
    internal static readonly KeyValuePair<string, object> QueueTag = new(CrestAppsTelemetry.QueueTag, nameof(SampleAIChatDocumentIndexingQueue));

    private readonly Channel<SampleAIChatDocumentIndexingWorkItem> _channel = Channel.CreateUnbounded<SampleAIChatDocumentIndexingWorkItem>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, });

    public ValueTask QueueIndexAsync(AIDocument document, IReadOnlyCollection<AIDocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);

        return EnqueueAsync(SampleAIChatDocumentIndexingWorkItem.ForIndex(document, chunks.ToArray()), cancellationToken);
    }

    public ValueTask QueueDeleteChunksAsync(IReadOnlyCollection<string> chunkIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);

        return EnqueueAsync(SampleAIChatDocumentIndexingWorkItem.ForDeleteChunks(chunkIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray()), cancellationToken);
    }

    private ValueTask EnqueueAsync(SampleAIChatDocumentIndexingWorkItem workItem, CancellationToken cancellationToken)
    {
        CrestAppsTelemetry.QueueDepth.Add(1, QueueTag);

        return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    internal IAsyncEnumerable<SampleAIChatDocumentIndexingWorkItem> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
