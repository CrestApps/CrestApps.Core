using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularWorkspaceDocumentEventHandlerTests
{
    [Fact]
    public async Task UploadedAsync_TabularDocument_InvalidatesReference()
    {
        var publisher = new Mock<ITabularWorkspaceInvalidationPublisher>();
        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        var handler = CreateHandler(publisher, artifactStore);

        await handler.UploadedAsync(new AIChatDocumentUploadContext
        {
            ReferenceId = "session-1",
            ReferenceType = AIReferenceTypes.Document.ChatSession,
            UploadedDocuments =
            [
                new AIChatUploadedDocument
                {
                    DocumentInfo = new ChatDocumentInfo
                    {
                        DocumentId = "doc-1",
                        FileName = "survey.csv",
                    },
                },
            ],
        }, TestContext.Current.CancellationToken);

        artifactStore.Verify(
            store => store.SaveAsync("doc-1", It.IsAny<TabularDocumentArtifact>(), It.IsAny<CancellationToken>()),
            Times.Once);
        publisher.Verify(
            cache => cache.PublishAsync(It.Is<TabularWorkspaceInvalidation>(invalidation =>
                invalidation.Kind == TabularWorkspaceInvalidation.ReferenceKind &&
                invalidation.ReferenceType == AIReferenceTypes.Document.ChatSession &&
                invalidation.ReferenceId == "session-1"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemovedAsync_NonTabularDocument_DoesNotInvalidateReference()
    {
        var publisher = new Mock<ITabularWorkspaceInvalidationPublisher>();
        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        var handler = CreateHandler(publisher, artifactStore);

        await handler.RemovedAsync(new AIChatDocumentRemoveContext
        {
            ReferenceId = "session-1",
            ReferenceType = AIReferenceTypes.Document.ChatSession,
            DocumentInfo = new ChatDocumentInfo
            {
                DocumentId = "doc-1",
                FileName = "notes.txt",
            },
        }, TestContext.Current.CancellationToken);

        artifactStore.Verify(
            store => store.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        publisher.Verify(
            cache => cache.PublishAsync(It.IsAny<TabularWorkspaceInvalidation>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static TabularWorkspaceDocumentEventHandler CreateHandler(
        Mock<ITabularWorkspaceInvalidationPublisher> publisher,
        Mock<ITabularDocumentArtifactStore> artifactStore)
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".csv", embeddable: false, isTabular: true));

        return new TabularWorkspaceDocumentEventHandler(Options.Create(options), artifactStore.Object, [publisher.Object]);
    }
}
