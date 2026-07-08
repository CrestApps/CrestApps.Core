using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class DefaultAIDataSourceChangeNotifierTests
{
    [Fact]
    public async Task QueueDocumentsAddedOrUpdatedAsync_QueuesIncrementalSyncWorkItem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var queue = new AIDataSourceIndexingQueue(NullLogger<AIDataSourceIndexingQueue>.Instance);
        var catalog = new Mock<ICatalog<AIDataSource>>();
        catalog.Setup(store => store.FindByIdAsync("ds-1", cancellationToken))
            .ReturnsAsync(new AIDataSource
            {
                ItemId = "ds-1",
                DisplayText = "External docs",
            });

        var notifier = new DefaultAIDataSourceChangeNotifier(catalog.Object, queue);

        await notifier.QueueDocumentsAddedOrUpdatedAsync("ds-1", ["doc-1", "doc-1", " ", "doc-2"], cancellationToken);

        var workItem = await ReadNextAsync(queue);

        Assert.Equal(AIDataSourceIndexingWorkItemType.SyncDataSourceDocuments, workItem.Type);
        Assert.Equal("ds-1", workItem.DataSourceId);
        Assert.Equal(["doc-1", "doc-2"], workItem.DocumentIds);
    }

    [Fact]
    public async Task QueueDocumentsDeletedAsync_ThrowsWhenDataSourceDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var queue = new AIDataSourceIndexingQueue(NullLogger<AIDataSourceIndexingQueue>.Instance);
        var catalog = new Mock<ICatalog<AIDataSource>>();
        catalog.Setup(store => store.FindByIdAsync("missing", cancellationToken))
            .ReturnsAsync((AIDataSource)null);

        var notifier = new DefaultAIDataSourceChangeNotifier(catalog.Object, queue);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            notifier.QueueDocumentsDeletedAsync("missing", ["doc-1"], cancellationToken).AsTask());

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
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
