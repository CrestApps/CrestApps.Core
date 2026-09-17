using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
            Options.Create(new DocumentFileSystemFileStoreOptions { BasePath = Path.Combine(Path.GetTempPath(), "cleanup-tests") }),
            NullLogger<DefaultConversationDocumentCleanupService>.Instance);

        await service.CleanupAsync("session-1", "chat-session", TestContext.Current.CancellationToken);

        foreach (var document in documents)
        {
            chunkStore.Verify(store => store.DeleteByDocumentIdAsync(document.ItemId), Times.Once);
            artifactStore.Verify(store => store.DeleteAsync(document.ItemId, It.IsAny<CancellationToken>()), Times.Once);
            fileStore.Verify(store => store.DeleteFileAsync(document.StoredFilePath), Times.Once);
            documentStore.Verify(store => store.DeleteAsync(document, It.IsAny<CancellationToken>()), Times.Once);
        }

        // The workspace database holds a copy of every spreadsheet, so deleting the conversation has
        // to take the database with it.
        foreach (var path in TabularDatabasePaths("chat-session", "session-1"))
        {
            fileStore.Verify(store => store.DeleteFileAsync(path), Times.Once);
        }
    }

    [Fact]
    public async Task CleanupAsync_WhenNoDocumentsRemain_StillDeletesTheTabularDatabase()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        var fileStore = new Mock<IDocumentFileStore>();

        // The conversation's spreadsheets were each removed individually, so no document rows remain.
        // The database file is separate storage and survives that, so cleanup still has to delete it.
        documentStore
            .Setup(store => store.GetDocumentsAsync("session-1", "chat-session"))
            .ReturnsAsync([]);

        var service = CreateService(documentStore, fileStore);

        await service.CleanupAsync("session-1", "chat-session", TestContext.Current.CancellationToken);

        foreach (var path in TabularDatabasePaths("chat-session", "session-1"))
        {
            fileStore.Verify(store => store.DeleteFileAsync(path), Times.Once);
        }
    }

    [Theory]
    [InlineData("chat-session")]
    [InlineData("chat-interaction")]
    public async Task CleanupAsync_DeletesOnlyTheTabularDatabaseOfTheScopeBeingCleaned(string referenceType)
    {
        var documentStore = new Mock<IAIDocumentStore>();
        var fileStore = new Mock<IDocumentFileStore>();

        documentStore
            .Setup(store => store.GetDocumentsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([]);

        var service = CreateService(documentStore, fileStore);

        await service.CleanupAsync("scope-1", referenceType, TestContext.Current.CancellationToken);

        foreach (var path in TabularDatabasePaths(referenceType, "scope-1"))
        {
            fileStore.Verify(store => store.DeleteFileAsync(path), Times.Once);
        }

        // Nothing outside the cleaned scope may be touched: not a sibling conversation, and not the
        // same identifier under the other reference type.
        var otherType = referenceType == "chat-session" ? "chat-interaction" : "chat-session";

        foreach (var path in TabularDatabasePaths(referenceType, "scope-2").Concat(TabularDatabasePaths(otherType, "scope-1")))
        {
            fileStore.Verify(store => store.DeleteFileAsync(path), Times.Never);
        }
    }

    [Theory]
    [InlineData("..")]
    [InlineData("scope-1/../scope-2")]
    [InlineData("scope 1")]
    public async Task CleanupAsync_WhenTheScopeIsNotASinglePathSegment_DeletesNoDatabase(string referenceId)
    {
        var documentStore = new Mock<IAIDocumentStore>();
        var fileStore = new Mock<IDocumentFileStore>();

        documentStore
            .Setup(store => store.GetDocumentsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([]);

        var service = CreateService(documentStore, fileStore);

        await service.CleanupAsync(referenceId, "chat-session", TestContext.Current.CancellationToken);

        fileStore.Verify(store => store.DeleteFileAsync(It.IsAny<string>()), Times.Never);
    }

    private static DefaultConversationDocumentCleanupService CreateService(
        Mock<IAIDocumentStore> documentStore,
        Mock<IDocumentFileStore> fileStore)
    {
        return new DefaultConversationDocumentCleanupService(
            documentStore.Object,
            Mock.Of<IAIDocumentChunkStore>(),
            fileStore.Object,
            Mock.Of<ITabularDocumentArtifactStore>(),
            Options.Create(new DocumentFileSystemFileStoreOptions { BasePath = Path.Combine(Path.GetTempPath(), "cleanup-tests") }),
            NullLogger<DefaultConversationDocumentCleanupService>.Instance);
    }

    private static string[] TabularDatabasePaths(string referenceType, string referenceId)
    {
        var databasePath = $"documents/{referenceType}/{referenceId}/data/tabular.db";

        return [databasePath, databasePath + "-wal", databasePath + "-shm"];
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
            Options.Create(new DocumentFileSystemFileStoreOptions { BasePath = Path.Combine(Path.GetTempPath(), "cleanup-tests") }),
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
            Options.Create(new DocumentFileSystemFileStoreOptions { BasePath = Path.Combine(Path.GetTempPath(), "cleanup-tests") }),
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
