using CrestApps.Core.AI.Documents;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.Services;

public sealed class SampleAIChatDocumentEventHandler : IAIChatDocumentEventHandler
{
    private readonly ISampleAIChatDocumentIndexingQueue _indexingQueue;

    public SampleAIChatDocumentEventHandler(ISampleAIChatDocumentIndexingQueue indexingQueue)
    {
        _indexingQueue = indexingQueue;
    }

    public async Task UploadedAsync(AIChatDocumentUploadContext context, CancellationToken cancellationToken = default)
    {
        foreach (var document in context.UploadedDocuments)
        {
            await _indexingQueue.QueueIndexAsync(document.Document, document.Chunks, cancellationToken);
        }
    }

    public async Task RemovedAsync(AIChatDocumentRemoveContext context, CancellationToken cancellationToken = default)
    {
        if (context.ChunkIds.Count == 0)
        {
            return;
        }

        await _indexingQueue.QueueDeleteChunksAsync(context.ChunkIds, cancellationToken);
    }
}
