using CrestApps.Core.Services;
using CrestApps.Core.Tests.Core.Services.Catalogs.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace CrestApps.Core.Tests.Core.Services.Catalogs;

public sealed class SourceCatalogManagerTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static SourceCatalogManager<TestNamedSourceCatalogEntry> CreateManager(List<TestNamedSourceCatalogEntry> records = null)
    {
        records ??= [];
        var catalog = InMemoryCatalogFactory.CreateNamedSourceCatalog(records);
        var logger = Mock.Of<ILogger<SourceCatalogManager<TestNamedSourceCatalogEntry>>>();

        return new SourceCatalogManager<TestNamedSourceCatalogEntry>(catalog, [], logger);
    }

    [Fact]
    public async Task GetAsync_ReturnsEntriesForRequestedSource()
    {
        var manager = CreateManager(
        [
            new TestNamedSourceCatalogEntry { ItemId = "1", Name = "First", Source = "A" },
            new TestNamedSourceCatalogEntry { ItemId = "2", Name = "Second", Source = "B" },
        ]);

        var entries = await manager.GetAsync("A", CancellationToken);

        Assert.Collection(entries, entry => Assert.Equal("1", entry.ItemId));
    }

    [Fact]
    public async Task FindBySourceAsync_ReturnsEntriesForRequestedSource()
    {
        var manager = CreateManager(
        [
            new TestNamedSourceCatalogEntry { ItemId = "1", Name = "First", Source = "A" },
            new TestNamedSourceCatalogEntry { ItemId = "2", Name = "Second", Source = "A" },
            new TestNamedSourceCatalogEntry { ItemId = "3", Name = "Third", Source = "B" },
        ]);

        var entries = await manager.FindBySourceAsync("A", CancellationToken);

        Assert.Equal(["1", "2"], entries.Select(entry => entry.ItemId));
    }

    [Fact]
    public async Task NewAsync_AssignsRequestedSource()
    {
        var manager = CreateManager();

        var entry = await manager.NewAsync("A", cancellationToken: CancellationToken);

        Assert.Equal("A", entry.Source);
        Assert.False(string.IsNullOrEmpty(entry.ItemId));
    }

    [Fact]
    public async Task NewAsync_RestoresRequestedSourceAfterInitialization()
    {
        var catalog = InMemoryCatalogFactory.CreateNamedSourceCatalog<TestNamedSourceCatalogEntry>([]);
        var logger = Mock.Of<ILogger<SourceCatalogManager<TestNamedSourceCatalogEntry>>>();
        var handler = new TestCatalogEntryHandler<TestNamedSourceCatalogEntry>
        {
            OnInitializingAsync = ctx =>
            {
                ctx.Model.Source = "Changed";

                return Task.CompletedTask;
            }
        };

        var manager = new SourceCatalogManager<TestNamedSourceCatalogEntry>(catalog, [handler], logger);

        var entry = await manager.NewAsync("A", cancellationToken: CancellationToken);

        Assert.Equal("A", entry.Source);
    }
}
