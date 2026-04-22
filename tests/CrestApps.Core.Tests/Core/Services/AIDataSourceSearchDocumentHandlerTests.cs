using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIDataSourceSearchDocumentHandlerTests
{
    [Fact]
    public async Task DocumentsAddedOrUpdatedAsync_QueuesMappedSourceDocuments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var sourceProfile = new SearchIndexProfile
        {
            ItemId = "profile-1",
            Name = "articles",
            Type = IndexProfileTypes.Articles,
        };

        var indexProfileManager = new Mock<ISearchIndexProfileManager>();
        indexProfileManager.Setup(manager => manager.FindByIdAsync(sourceProfile.ItemId))
            .ReturnsAsync(sourceProfile);

        var dataSourceCatalog = new Mock<ICatalog<AIDataSource>>();
        dataSourceCatalog.Setup(catalog => catalog.GetAllAsync())
            .ReturnsAsync(
            [
                new AIDataSource
                {
                    ItemId = "ds-1",
                    SourceIndexProfileName = "articles",
                    AIKnowledgeBaseIndexProfileName = "kb-index",
                },
            ]);

        var services = new ServiceCollection()
            .AddSingleton(indexProfileManager.Object)
            .AddSingleton(dataSourceCatalog.Object)
            .BuildServiceProvider();
        var queue = new AIDataSourceIndexingQueue(NullLogger<AIDataSourceIndexingQueue>.Instance);
        var handler = new AIDataSourceSearchDocumentHandler(queue, services, NullLogger<AIDataSourceSearchDocumentHandler>.Instance);

        await handler.DocumentsAddedOrUpdatedAsync(sourceProfile, ["doc-1", "doc-2"], cancellationToken);

        var workItem = await ReadNextAsync(queue);

        Assert.Equal(AIDataSourceIndexingWorkItemType.SyncSourceDocuments, workItem.Type);
        Assert.Equal("articles", workItem.SourceIndexProfileName);
        Assert.Equal(["doc-1", "doc-2"], workItem.DocumentIds);
    }

    [Fact]
    public async Task DocumentsDeletedAsync_IgnoresProfilesWithoutMappedDataSources()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var sourceProfile = new SearchIndexProfile
        {
            ItemId = "profile-2",
            Name = "articles",
            Type = IndexProfileTypes.Articles,
        };

        var indexProfileManager = new Mock<ISearchIndexProfileManager>();
        indexProfileManager.Setup(manager => manager.FindByIdAsync(sourceProfile.ItemId))
            .ReturnsAsync(sourceProfile);

        var dataSourceCatalog = new Mock<ICatalog<AIDataSource>>();
        dataSourceCatalog.Setup(catalog => catalog.GetAllAsync())
            .ReturnsAsync(
            [
                new AIDataSource
                {
                    ItemId = "ds-1",
                    SourceIndexProfileName = "posts",
                    AIKnowledgeBaseIndexProfileName = "kb-index",
                },
            ]);

        var services = new ServiceCollection()
            .AddSingleton(indexProfileManager.Object)
            .AddSingleton(dataSourceCatalog.Object)
            .BuildServiceProvider();
        var queue = new AIDataSourceIndexingQueue(NullLogger<AIDataSourceIndexingQueue>.Instance);
        var handler = new AIDataSourceSearchDocumentHandler(queue, services, NullLogger<AIDataSourceSearchDocumentHandler>.Instance);

        await handler.DocumentsDeletedAsync(sourceProfile, ["doc-1"], cancellationToken);

        Assert.False(await HasWorkItemAsync(queue));
    }

    private static async Task<AIDataSourceIndexingWorkItem> ReadNextAsync(AIDataSourceIndexingQueue queue)
    {
        await using var enumerator = queue.ReadAllAsync().GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());

        return enumerator.Current;
    }

    private static async Task<bool> HasWorkItemAsync(AIDataSourceIndexingQueue queue)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        try
        {
            await using var enumerator = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);

            return await enumerator.MoveNextAsync();
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
