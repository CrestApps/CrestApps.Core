using CrestApps.Core.Services;
using CrestApps.Core.Tests.Core.Services.Catalogs.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace CrestApps.Core.Tests.Core.Services.Catalogs;

public sealed class NamedCatalogManagerTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static NamedCatalogManager<TestNamedCatalogEntry> CreateManager(List<TestNamedCatalogEntry> records = null)
    {
        records ??= [];
        var catalog = InMemoryCatalogFactory.CreateNamedCatalog(records);
        var logger = Mock.Of<ILogger<NamedCatalogManager<TestNamedCatalogEntry>>>();

        return new NamedCatalogManager<TestNamedCatalogEntry>(catalog, [], logger);
    }

    [Fact]
    public async Task FindByNameAsync_ReturnsEntry_WhenExists()
    {
        var entry = new TestNamedCatalogEntry { ItemId = "1", Name = "Test" };
        var manager = CreateManager([entry]);

        var result = await manager.FindByNameAsync("Test", CancellationToken);

        Assert.Equal(entry, result);
    }

    [Fact]
    public async Task CreateAsync_AddsEntry()
    {
        var records = new List<TestNamedCatalogEntry>();
        var manager = CreateManager(records);
        var entry = new TestNamedCatalogEntry { ItemId = "new", Name = "Test" };

        await manager.CreateAsync(entry, CancellationToken);

        var result = await manager.FindByNameAsync("Test", CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(entry, result);
    }

    [Fact]
    public async Task FindByNameAsync_InvokesLoadedHandler()
    {
        var entry = new TestNamedCatalogEntry { ItemId = "1", Name = "Test" };
        var records = new List<TestNamedCatalogEntry> { entry };
        var catalog = InMemoryCatalogFactory.CreateNamedCatalog(records);
        var logger = Mock.Of<ILogger<NamedCatalogManager<TestNamedCatalogEntry>>>();
        var callOrder = new Queue<string>();
        var existsInCatalogDuringLoaded = false;

        var handler = new TestCatalogEntryHandler<TestNamedCatalogEntry>
        {
            OnLoadedAsync = async ctx =>
            {
                existsInCatalogDuringLoaded = await catalog.FindByNameAsync(entry.Name, CancellationToken) != null;
                callOrder.Enqueue("LoadedAsync");
            }
        };

        var manager = new NamedCatalogManager<TestNamedCatalogEntry>(catalog, [handler], logger);

        await manager.FindByNameAsync("Test", CancellationToken);

        Assert.Equal("LoadedAsync", callOrder.Dequeue());
        Assert.Empty(callOrder);
        Assert.True(existsInCatalogDuringLoaded);
    }
}
