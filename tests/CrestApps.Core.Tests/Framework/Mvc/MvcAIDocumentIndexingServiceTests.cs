using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Mvc.Web.Areas.Indexing.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class MvcAIDocumentIndexingServiceTests
{
    [Fact]
    public async Task IndexAsync_WhenChunksDoNotContainEmbeddingsOrContent_SkipsIndexing()
    {
        var indexProfileStore = new Mock<ISearchIndexProfileStore>(MockBehavior.Strict);
        var indexManager = new Mock<ISearchIndexManager>(MockBehavior.Strict);
        var documentManager = new Mock<ISearchDocumentManager>(MockBehavior.Strict);

        var service = CreateService(indexProfileStore.Object, indexManager.Object, documentManager.Object);

        await service.IndexAsync(
            CreateDocument(),
            [
                CreateChunk("chunk-1", content: "", embedding: [0.1f, 0.2f]),
                CreateChunk("chunk-2", content: "Has content", embedding: []),
                CreateChunk("chunk-3", content: "   ", embedding: []),
            ],
            TestContext.Current.CancellationToken);

        indexProfileStore.Verify(store => store.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        indexManager.VerifyNoOtherCalls();
        documentManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IndexAsync_WhenIndexDoesNotExist_CreatesIndexAndWritesOnlyValidChunks()
    {
        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore
            .Setup(store => store.FindByNameAsync("chat-documents", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIndexProfile());

        var indexManager = new Mock<ISearchIndexManager>();
        indexManager
            .Setup(manager => manager.ExistsAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        IIndexProfileInfo createdProfile = null;
        IReadOnlyCollection<SearchIndexField> createdFields = [];
        IIndexProfileInfo updatedProfile = null;
        IReadOnlyCollection<IndexDocument> indexedDocuments = [];

        var documentManager = new Mock<ISearchDocumentManager>();
        documentManager
            .Setup(manager => manager.AddOrUpdateAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<IReadOnlyCollection<IndexDocument>>(), It.IsAny<CancellationToken>()))
            .Callback<IIndexProfileInfo, IReadOnlyCollection<IndexDocument>, CancellationToken>((profile, documents, _) =>
            {
                updatedProfile = profile;
                indexedDocuments = documents;
            })
            .ReturnsAsync(true);

        indexManager
            .Setup(manager => manager.CreateAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<IReadOnlyCollection<SearchIndexField>>(), It.IsAny<CancellationToken>()))
            .Callback<IIndexProfileInfo, IReadOnlyCollection<SearchIndexField>, CancellationToken>((profile, fields, _) =>
            {
                createdProfile = profile;
                createdFields = fields;
            })
            .Returns(Task.CompletedTask);

        var service = CreateService(indexProfileStore.Object, indexManager.Object, documentManager.Object);
        var document = CreateDocument();

        await service.IndexAsync(
            document,
            [
                CreateChunk("chunk-1", index: 0),
                CreateChunk("chunk-2", index: 1, embedding: [0.3f, 0.4f]),
                CreateChunk("chunk-3", content: "", embedding: [0.5f, 0.6f]),
                CreateChunk("chunk-4", content: "Missing vector", embedding: []),
            ],
            TestContext.Current.CancellationToken);

        indexManager.Verify(manager => manager.CreateAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<IReadOnlyCollection<SearchIndexField>>(), It.IsAny<CancellationToken>()), Times.Once);

        documentManager.Verify(manager => manager.AddOrUpdateAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<IReadOnlyCollection<IndexDocument>>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("chat-documents", Assert.IsType<SearchIndexProfile>(createdProfile).Name);
        Assert.Contains(createdFields, field =>
            field.Name == DocumentIndexConstants.ColumnNames.Embedding &&
            field.FieldType == SearchFieldType.Vector &&
            field.VectorDimensions == 2);
        Assert.Equal("chat-documents", Assert.IsType<SearchIndexProfile>(updatedProfile).Name);
        var documents = Assert.IsAssignableFrom<IReadOnlyCollection<IndexDocument>>(indexedDocuments);
        Assert.Equal(2, documents.Count);

        var firstDocument = Assert.Single(documents, entry => entry.Id == "chunk-1");
        Assert.Equal("document-1", firstDocument.Fields[DocumentIndexConstants.ColumnNames.DocumentId]);
        Assert.Equal("story.pdf", firstDocument.Fields[DocumentIndexConstants.ColumnNames.FileName]);
        Assert.Equal("Car race story", firstDocument.Fields[DocumentIndexConstants.ColumnNames.Content]);
        Assert.Equal(0, firstDocument.Fields[DocumentIndexConstants.ColumnNames.ChunkIndex]);
    }

    [Fact]
    public async Task IndexAsync_WhenConfiguredProfileHasWrongType_DoesNotCallProviderServices()
    {
        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore
            .Setup(store => store.FindByNameAsync("chat-documents", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIndexProfile(type: "ChatHistory"));

        var indexManager = new Mock<ISearchIndexManager>(MockBehavior.Strict);
        var documentManager = new Mock<ISearchDocumentManager>(MockBehavior.Strict);

        var service = CreateService(indexProfileStore.Object, indexManager.Object, documentManager.Object);

        await service.IndexAsync(
            CreateDocument(),
            [CreateChunk("chunk-1")],
            TestContext.Current.CancellationToken);

        indexManager.VerifyNoOtherCalls();
        documentManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IndexAsync_WhenDocumentManagerThrows_AttemptsWriteWithoutBubblingFailure()
    {
        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore
            .Setup(store => store.FindByNameAsync("chat-documents", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIndexProfile());

        var indexManager = new Mock<ISearchIndexManager>();
        indexManager
            .Setup(manager => manager.ExistsAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var documentManager = new Mock<ISearchDocumentManager>();
        documentManager
            .Setup(manager => manager.AddOrUpdateAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<IReadOnlyCollection<IndexDocument>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Index backend unavailable."));

        var service = CreateService(indexProfileStore.Object, indexManager.Object, documentManager.Object);

        await service.IndexAsync(CreateDocument(), [CreateChunk("chunk-1")], TestContext.Current.CancellationToken);

        documentManager.Verify(
            manager => manager.AddOrUpdateAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<IReadOnlyCollection<IndexDocument>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenProviderIsConfigured_DeletesRequestedDocument()
    {
        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore
            .Setup(store => store.FindByNameAsync("chat-documents", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIndexProfile());

        IEnumerable<string> deletedIds = [];

        var documentManager = new Mock<ISearchDocumentManager>();
        documentManager
            .Setup(manager => manager.DeleteAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IIndexProfileInfo, IEnumerable<string>, CancellationToken>((_, ids, _) => deletedIds = ids.ToArray())
            .Returns(Task.CompletedTask);

        var service = CreateService(indexProfileStore.Object, Mock.Of<ISearchIndexManager>(), documentManager.Object);

        await service.DeleteAsync("document-7", TestContext.Current.CancellationToken);

        documentManager.Verify(manager => manager.DeleteAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(["document-7"], Assert.IsAssignableFrom<IEnumerable<string>>(deletedIds));
    }

    [Fact]
    public async Task DeleteChunksAsync_WhenIdsContainDuplicatesOrWhitespace_DeletesDistinctIds()
    {
        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore
            .Setup(store => store.FindByNameAsync("chat-documents", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIndexProfile());

        IEnumerable<string> deletedIds = [];

        var documentManager = new Mock<ISearchDocumentManager>();
        documentManager
            .Setup(manager => manager.DeleteAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IIndexProfileInfo, IEnumerable<string>, CancellationToken>((_, ids, _) => deletedIds = ids.ToArray())
            .Returns(Task.CompletedTask);

        var service = CreateService(indexProfileStore.Object, Mock.Of<ISearchIndexManager>(), documentManager.Object);

        await service.DeleteChunksAsync(["chunk-1", "", "chunk-2", "chunk-1", "   "], TestContext.Current.CancellationToken);

        var actualIds = Assert.IsAssignableFrom<IEnumerable<string>>(deletedIds).ToArray();
        Assert.Equal(["chunk-1", "chunk-2"], actualIds);
    }

    [Fact]
    public async Task DeleteChunksAsync_WhenNoUsableIds_DoesNotLookupProfile()
    {
        var indexProfileStore = new Mock<ISearchIndexProfileStore>(MockBehavior.Strict);
        var service = CreateService(indexProfileStore.Object, Mock.Of<ISearchIndexManager>(), Mock.Of<ISearchDocumentManager>());

        await service.DeleteChunksAsync(["", " "], TestContext.Current.CancellationToken);

        indexProfileStore.Verify(store => store.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SampleAIDocumentIndexingService CreateService(
        ISearchIndexProfileStore indexProfileStore,
        ISearchIndexManager indexManager,
        ISearchDocumentManager documentManager)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISearchIndexManager>(AISearchConstants.ProviderName, indexManager);
        services.AddKeyedSingleton<ISearchDocumentManager>(AISearchConstants.ProviderName, documentManager);

        return new SampleAIDocumentIndexingService(
                    Options.Create(new InteractionDocumentOptions { IndexProfileName = "chat-documents" }),
                    indexProfileStore,
                    services.BuildServiceProvider(),
                    NullLogger<SampleAIDocumentIndexingService>.Instance);
    }

    private static SearchIndexProfile CreateIndexProfile(string type = IndexProfileTypes.AIDocuments)
    {
        return new SearchIndexProfile
        {
            ItemId = "profile-1",
            Name = "chat-documents",
            ProviderName = AISearchConstants.ProviderName,
            Type = type,
            IndexFullName = "chat-documents-index",
        };
    }

    private static AIDocument CreateDocument()
    {
        return new AIDocument
        {
            ItemId = "document-1",
            FileName = "story.pdf",
        };
    }

    private static AIDocumentChunk CreateChunk(string itemId, string content = "Car race story", float[] embedding = null, int index = 0)
    {
        return new AIDocumentChunk
        {
            ItemId = itemId,
            AIDocumentId = "document-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            Content = content,
            Embedding = embedding ?? [0.1f, 0.2f],
            Index = index,
        };
    }
}
