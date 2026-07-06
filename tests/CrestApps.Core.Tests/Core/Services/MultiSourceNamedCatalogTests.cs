using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
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
