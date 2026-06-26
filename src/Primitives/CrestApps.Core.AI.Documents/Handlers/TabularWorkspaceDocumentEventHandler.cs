using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Handlers;

internal sealed class TabularWorkspaceDocumentEventHandler : IAIChatDocumentEventHandler
{
    private readonly ChatDocumentsOptions _documentOptions;
    private readonly ITabularDocumentArtifactStore _artifactStore;
    private readonly IEnumerable<ITabularWorkspaceInvalidationPublisher> _invalidationPublishers;

    public TabularWorkspaceDocumentEventHandler(
        IOptions<ChatDocumentsOptions> documentOptions,
        ITabularDocumentArtifactStore artifactStore,
        IEnumerable<ITabularWorkspaceInvalidationPublisher> invalidationPublishers)
    {
        _documentOptions = documentOptions.Value;
        _artifactStore = artifactStore;
        _invalidationPublishers = invalidationPublishers;
    }

    public Task UploadedAsync(AIChatDocumentUploadContext context, CancellationToken cancellationToken = default)
    {
        if (context?.UploadedDocuments is null)
        {
            return Task.CompletedTask;
        }

        return UploadedCoreAsync(context, cancellationToken);
    }

    public async Task RemovedAsync(AIChatDocumentRemoveContext context, CancellationToken cancellationToken = default)
    {
        if (IsTabular(context?.DocumentInfo))
        {
            await _artifactStore.DeleteAsync(context.DocumentInfo.DocumentId, cancellationToken);
            await _invalidationPublishers.PublishAllAsync(
                TabularWorkspaceInvalidation.ForReference(context.ReferenceType, context.ReferenceId),
                cancellationToken);
        }
    }

    private async Task UploadedCoreAsync(AIChatDocumentUploadContext context, CancellationToken cancellationToken)
    {
        var hasTabularDocuments = false;

        foreach (var uploadedDocument in context.UploadedDocuments)
        {
            if (!IsTabular(uploadedDocument.DocumentInfo))
            {
                continue;
            }

            var content = string.Concat(uploadedDocument.Chunks.OrderBy(chunk => chunk.Index).Select(chunk => chunk.Content));
            var artifact = TabularDocumentArtifact.FromDelimitedContent(content, uploadedDocument.DocumentInfo.FileName);
            await _artifactStore.SaveAsync(uploadedDocument.DocumentInfo.DocumentId, artifact, cancellationToken);
            hasTabularDocuments = true;
        }

        if (hasTabularDocuments)
        {
            await _invalidationPublishers.PublishAllAsync(
                TabularWorkspaceInvalidation.ForReference(context.ReferenceType, context.ReferenceId),
                cancellationToken);
        }
    }

    private bool IsTabular(ChatDocumentInfo document)
    {
        return document is not null && _documentOptions.IsTabularFileExtension(document.FileName);
    }
}
