using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularWorkspaceDocumentEventHandlerTests
{
    [Fact]
    public async Task UploadedAsync_TabularDocument_DoesNotSaveArtifact()
    {
        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        var handler = CreateHandler(artifactStore);

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
            store => store.SaveAsync(It.IsAny<string>(), It.IsAny<TabularDocumentArtifact>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RemovedAsync_NonTabularDocument_DoesNotDeleteArtifact()
    {
        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        var handler = CreateHandler(artifactStore);

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
    }

    [Fact]
    public async Task RemovedAsync_TabularDocument_DeletesArtifact()
    {
        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        var handler = CreateHandler(artifactStore);

        await handler.RemovedAsync(new AIChatDocumentRemoveContext
        {
            ReferenceId = "session-1",
            ReferenceType = AIReferenceTypes.Document.ChatSession,
            DocumentInfo = new ChatDocumentInfo
            {
                DocumentId = "doc-1",
                FileName = "data.csv",
            },
        }, TestContext.Current.CancellationToken);

        artifactStore.Verify(
            store => store.DeleteAsync("doc-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static TabularWorkspaceDocumentEventHandler CreateHandler(Mock<ITabularDocumentArtifactStore> artifactStore)
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".csv", embeddable: false, isTabular: true));

        var fileStoreOptions = new DocumentFileSystemFileStoreOptions
        {
            BasePath = Path.Combine(Path.GetTempPath(), "tabular-doc-event-tests"),
        };

        return new TabularWorkspaceDocumentEventHandler(
            Options.Create(options),
            artifactStore.Object,
            Options.Create(fileStoreOptions),
            NullLogger<TabularWorkspaceDocumentEventHandler>.Instance);
    }
}
