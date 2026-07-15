using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class MultiSourceNamedCatalogTests
{
    [Fact]
    public async Task GetAllAsync_CachesMergedEntriesWithinStoreInstance()
    {
        // Arrange
        var source = new CountingConnectionSource(
        [
            new AIProviderConnection
            {
                ItemId = "connection-1",
                Name = "winnerware-sys",
                ClientName = "Azure",
                Source = "Azure",
            },
        ]);
        var store = new DefaultAIProviderConnectionStore([source]);

        // Act
        var first = await store.GetAllAsync(TestContext.Current.CancellationToken);
        var second = await store.GetAllAsync(TestContext.Current.CancellationToken);
        var lookup = await store.FindByNameAsync("winnerware-sys", TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(first);
        Assert.Single(second);
        Assert.NotNull(lookup);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task CreateAsync_InvalidatesMergedEntryCache()
    {
        // Arrange
        var source = new CountingConnectionSource(
        [
            new AIProviderConnection
            {
                ItemId = "connection-1",
                Name = "winnerware-sys",
                ClientName = "Azure",
                Source = "Azure",
            },
        ]);
        var store = new DefaultAIProviderConnectionStore([source]);

        _ = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Act
        await store.CreateAsync(new AIProviderConnection
        {
            ItemId = "connection-2",
            Name = "WinnerWare",
            ClientName = "Azure",
            Source = "Azure",
        }, TestContext.Current.CancellationToken);

        var entries = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, source.ReadCount);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task GetAsync_ReturnsCaseInsensitiveIdMatchesInCatalogOrder()
    {
        var source = new CountingConnectionSource(
        [
            CreateConnection("connection-1", "first"),
            CreateConnection("connection-2", "second"),
            CreateConnection("connection-3", "third"),
        ]);
        var store = new DefaultAIProviderConnectionStore([source]);

        var entries = await store.GetAsync(
            ["CONNECTION-3", "connection-1"],
            TestContext.Current.CancellationToken);

        Assert.Equal(["connection-1", "connection-3"], entries.Select(entry => entry.ItemId));
    }

    [Fact]
    public async Task PageAsync_ReturnsFilteredSortedPageAndTotalCount()
    {
        var source = new CountingConnectionSource(
        [
            CreateConnection("connection-1", "Zulu"),
            CreateConnection("connection-2", "Alpha"),
            CreateConnection("connection-3", "Alpine"),
            CreateConnection("connection-4", "Beta"),
        ]);
        var store = new DefaultAIProviderConnectionStore([source]);

        var page = await store.PageAsync(
            2,
            1,
            new QueryContext
            {
                Name = "Al",
                Sorted = true,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Count);
        var entry = Assert.Single(page.Entries);
        Assert.Equal("Alpine", entry.Name);
    }

    [Fact]
    public async Task PageAsync_ReturnsUnfilteredPageInCatalogOrder()
    {
        var source = new CountingConnectionSource(
        [
            CreateConnection("connection-1", "first"),
            CreateConnection("connection-2", "second"),
            CreateConnection("connection-3", "third"),
        ]);
        var store = new DefaultAIProviderConnectionStore([source]);

        var page = await store.PageAsync(
            2,
            1,
            new QueryContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, page.Count);
        var entry = Assert.Single(page.Entries);
        Assert.Equal("connection-2", entry.ItemId);
    }

    [Fact]
    public async Task PageAsync_AppliesSourceFilterFromDerivedCatalog()
    {
        var source = new CountingConnectionSource(
        [
            CreateConnection("connection-1", "first", "Azure"),
            CreateConnection("connection-2", "second", "OpenAI"),
            CreateConnection("connection-3", "third", "Azure"),
        ]);
        var store = new DefaultAIProviderConnectionStore([source]);

        var page = await store.PageAsync(
            1,
            10,
            new QueryContext
            {
                Source = "OpenAI",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, page.Count);
        var entry = Assert.Single(page.Entries);
        Assert.Equal("connection-2", entry.ItemId);
    }

    private static AIProviderConnection CreateConnection(
        string itemId,
        string name,
        string source = "Azure")
    {
        return new AIProviderConnection
        {
            ItemId = itemId,
            Name = name,
            ClientName = "Azure",
            Source = source,
        };
    }

    private sealed class CountingConnectionSource(List<AIProviderConnection> entries) : IWritableNamedSourceCatalogSource<AIProviderConnection>
    {
        public int ReadCount { get; private set; }

        public int Order => 0;

        public ValueTask<IReadOnlyCollection<AIProviderConnection>> GetEntriesAsync(
            IReadOnlyCollection<AIProviderConnection> knownEntries,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;

            return ValueTask.FromResult<IReadOnlyCollection<AIProviderConnection>>(entries.ToArray());
        }

        public ValueTask CreateAsync(AIProviderConnection entry, CancellationToken cancellationToken = default)
        {
            entries.Add(entry);

            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(AIProviderConnection entry, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(entries.Remove(entry));
        }

        public ValueTask UpdateAsync(AIProviderConnection entry, CancellationToken cancellationToken = default)
        {
            var index = entries.FindIndex(existing => string.Equals(existing.ItemId, entry.ItemId, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                entries[index] = entry;
            }

            return ValueTask.CompletedTask;
        }
    }
}
