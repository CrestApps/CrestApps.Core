using CrestApps.Core.Services;
using CrestApps.Core.Tests.Core.Services.Catalogs.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace CrestApps.Core.Tests.Core.Services.Catalogs;

public sealed class NamedSourceCatalogManagerTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static NamedSourceCatalogManager<TestNamedSourceCatalogEntry> CreateManager(List<TestNamedSourceCatalogEntry> records = null)
    {
        records ??= [];
        var catalog = InMemoryCatalogFactory.CreateNamedSourceCatalog(records);
        var logger = Mock.Of<ILogger<NamedSourceCatalogManager<TestNamedSourceCatalogEntry>>>();

        return new NamedSourceCatalogManager<TestNamedSourceCatalogEntry>(catalog, [], logger);
    }

    [Fact]
    public async Task FindByNameAsync_ReturnsEntry_WhenExists()
    {
        var entry = new TestNamedSourceCatalogEntry { ItemId = "1", Name = "Test", Source = "A" };
        var manager = CreateManager([entry]);

        var result = await manager.FindByNameAsync("Test", CancellationToken);

        Assert.Equal(entry, result);
    }

    [Fact]
    public async Task GetAsync_ReturnsEntry_WhenExists()
    {
        var entry = new TestNamedSourceCatalogEntry { ItemId = "1", Name = "Test", Source = "A" };
        var manager = CreateManager([entry]);

        var result = await manager.GetAsync("Test", "A", CancellationToken);

        Assert.Equal(entry, result);
    }

    [Fact]
    public async Task FindByNameAsync_InvokesLoadedHandler()
    {
        var entry = new TestNamedSourceCatalogEntry { ItemId = "1", Name = "Test", Source = "A" };
        var records = new List<TestNamedSourceCatalogEntry> { entry };
        var catalog = InMemoryCatalogFactory.CreateNamedSourceCatalog(records);
        var logger = Mock.Of<ILogger<NamedSourceCatalogManager<TestNamedSourceCatalogEntry>>>();
        var callOrder = new Queue<string>();
        var existsInCatalogDuringLoaded = false;

        var handler = new TestCatalogEntryHandler<TestNamedSourceCatalogEntry>
        {
            OnLoadedAsync = async ctx =>
            {
                existsInCatalogDuringLoaded = await catalog.FindByNameAsync(entry.Name, CancellationToken) != null;
                callOrder.Enqueue("LoadedAsync");
            }
        };

        var manager = new NamedSourceCatalogManager<TestNamedSourceCatalogEntry>(catalog, [handler], logger);

        await manager.FindByNameAsync("Test", CancellationToken);

        Assert.Equal("LoadedAsync", callOrder.Dequeue());
        Assert.Empty(callOrder);
        Assert.True(existsInCatalogDuringLoaded);
    }

    [Fact]
    public async Task GetAsync_InvokesLoadedHandler()
    {
        var entry = new TestNamedSourceCatalogEntry { ItemId = "1", Name = "Test", Source = "A" };
        var records = new List<TestNamedSourceCatalogEntry> { entry };
        var catalog = InMemoryCatalogFactory.CreateNamedSourceCatalog(records);
        var logger = Mock.Of<ILogger<NamedSourceCatalogManager<TestNamedSourceCatalogEntry>>>();
        var callOrder = new Queue<string>();
        var existsInCatalogDuringLoaded = false;

        var handler = new TestCatalogEntryHandler<TestNamedSourceCatalogEntry>
        {
            OnLoadedAsync = async ctx =>
            {
                existsInCatalogDuringLoaded = await catalog.GetAsync(entry.Name, entry.Source, CancellationToken) != null;
                callOrder.Enqueue("LoadedAsync");
            }
        };

        var manager = new NamedSourceCatalogManager<TestNamedSourceCatalogEntry>(catalog, [handler], logger);

        await manager.GetAsync("Test", "A", CancellationToken);

        Assert.Equal("LoadedAsync", callOrder.Dequeue());
        Assert.Empty(callOrder);
        Assert.True(existsInCatalogDuringLoaded);
    }
}
