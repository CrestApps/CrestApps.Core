using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class ConversationDocumentCleanupServiceTests
{
    [Fact]
    public async Task CleanupAsync_RemovesDocumentsFilesArtifactsAndChunks()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        var chunkStore = new Mock<IAIDocumentChunkStore>();
        var fileStore = new Mock<IDocumentFileStore>();
        var artifactStore = new Mock<ITabularDocumentArtifactStore>();

        var documents = new List<AIDocument>
        {
            new() { ItemId = "doc-1", StoredFilePath = "documents/chat-session/session-1/a.xlsx" },
            new() { ItemId = "doc-2", StoredFilePath = "documents/chat-session/session-1/b.csv" },
        };

        documentStore
            .Setup(store => store.GetDocumentsAsync("session-1", "chat-session"))
            .ReturnsAsync(documents);

        var service = new DefaultConversationDocumentCleanupService(
            documentStore.Object,
            chunkStore.Object,
            fileStore.Object,
            artifactStore.Object,
            NullLogger<DefaultConversationDocumentCleanupService>.Instance);

        await service.CleanupAsync("session-1", "chat-session", TestContext.Current.CancellationToken);

        foreach (var document in documents)
        {
            chunkStore.Verify(store => store.DeleteByDocumentIdAsync(document.ItemId), Times.Once);
            artifactStore.Verify(store => store.DeleteAsync(document.ItemId, It.IsAny<CancellationToken>()), Times.Once);
            fileStore.Verify(store => store.DeleteFileAsync(document.StoredFilePath), Times.Once);
            documentStore.Verify(store => store.DeleteAsync(document, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task CleanupAsync_WhenNoDocuments_DoesNothing()
    {
        var documentStore = new Mock<IAIDocumentStore>(MockBehavior.Strict);
        var chunkStore = new Mock<IAIDocumentChunkStore>(MockBehavior.Strict);
        var fileStore = new Mock<IDocumentFileStore>(MockBehavior.Strict);
        var artifactStore = new Mock<ITabularDocumentArtifactStore>(MockBehavior.Strict);

        documentStore
            .Setup(store => store.GetDocumentsAsync("session-1", "chat-session"))
            .ReturnsAsync([]);

        var service = new DefaultConversationDocumentCleanupService(
            documentStore.Object,
            chunkStore.Object,
            fileStore.Object,
            artifactStore.Object,
            NullLogger<DefaultConversationDocumentCleanupService>.Instance);

        await service.CleanupAsync("session-1", "chat-session", TestContext.Current.CancellationToken);

        documentStore.Verify(store => store.GetDocumentsAsync("session-1", "chat-session"), Times.Once);
        documentStore.VerifyNoOtherCalls();
        chunkStore.VerifyNoOtherCalls();
        fileStore.VerifyNoOtherCalls();
        artifactStore.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, "chat-session")]
    [InlineData("session-1", null)]
    [InlineData("", "chat-session")]
    public async Task CleanupAsync_WhenReferenceIsMissing_DoesNotQueryStore(string referenceId, string referenceType)
    {
        var documentStore = new Mock<IAIDocumentStore>(MockBehavior.Strict);

        var service = new DefaultConversationDocumentCleanupService(
            documentStore.Object,
            Mock.Of<IAIDocumentChunkStore>(),
            Mock.Of<IDocumentFileStore>(),
            Mock.Of<ITabularDocumentArtifactStore>(),
            NullLogger<DefaultConversationDocumentCleanupService>.Instance);

        await service.CleanupAsync(referenceId, referenceType, TestContext.Current.CancellationToken);

        documentStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CleanupGeneratedDocumentsAsync_RemovesOnlyGeneratedDocuments()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        var chunkStore = new Mock<IAIDocumentChunkStore>();
        var fileStore = new Mock<IDocumentFileStore>();
        var artifactStore = new Mock<ITabularDocumentArtifactStore>();

        var generated = new AIDocument
        {
            ItemId = "gen-1",
            StoredFilePath = "documents/chat-interaction/interaction-1/export.xlsx",
        };
        generated.Properties[DefaultGeneratedDocumentService.GeneratedPropertyName] = true;

        var uploaded = new AIDocument
        {
            ItemId = "up-1",
            StoredFilePath = "documents/chat-interaction/interaction-1/source.xlsx",
        };

        documentStore
            .Setup(store => store.FindByIdAsync("gen-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(generated);
        documentStore
            .Setup(store => store.FindByIdAsync("up-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(uploaded);
        documentStore
            .Setup(store => store.FindByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AIDocument)null);

        var service = new DefaultConversationDocumentCleanupService(
            documentStore.Object,
            chunkStore.Object,
            fileStore.Object,
            artifactStore.Object,
            NullLogger<DefaultConversationDocumentCleanupService>.Instance);

        await service.CleanupGeneratedDocumentsAsync(
            ["gen-1", "up-1", "missing", "gen-1"],
            TestContext.Current.CancellationToken);

        chunkStore.Verify(store => store.DeleteByDocumentIdAsync("gen-1"), Times.Once);
        artifactStore.Verify(store => store.DeleteAsync("gen-1", It.IsAny<CancellationToken>()), Times.Once);
        fileStore.Verify(store => store.DeleteFileAsync(generated.StoredFilePath), Times.Once);
        documentStore.Verify(store => store.DeleteAsync(generated, It.IsAny<CancellationToken>()), Times.Once);

        // The uploaded source document must never be removed.
        chunkStore.Verify(store => store.DeleteByDocumentIdAsync("up-1"), Times.Never);
        documentStore.Verify(store => store.DeleteAsync(uploaded, It.IsAny<CancellationToken>()), Times.Never);
        fileStore.Verify(store => store.DeleteFileAsync(uploaded.StoredFilePath), Times.Never);
    }
}
