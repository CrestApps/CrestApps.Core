using CrestApps.Core.AI;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class IndexingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCoreIndexingServices_RegistersFallbackIndexingServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCoreIndexingServices();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;

        var store = scopedServices.GetRequiredService<ISearchIndexProfileStore>();

        Assert.IsType<NullSearchIndexProfileStore>(store);
        Assert.Same(store, scopedServices.GetRequiredService<ICatalog<SearchIndexProfile>>());
        Assert.Same(store, scopedServices.GetRequiredService<INamedCatalog<SearchIndexProfile>>());
        Assert.IsType<SearchIndexProfileManager>(scopedServices.GetRequiredService<ISearchIndexProfileManager>());
        Assert.IsType<SearchIndexProfileProvisioningService>(scopedServices.GetRequiredService<ISearchIndexProfileProvisioningService>());
    }

    [Fact]
    public void AddCoreAIServices_RegistersIndexingFallbackServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCoreAIServices();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;

        Assert.IsType<NullSearchIndexProfileStore>(scopedServices.GetRequiredService<ISearchIndexProfileStore>());
        Assert.IsType<SearchIndexProfileManager>(scopedServices.GetRequiredService<ISearchIndexProfileManager>());
        Assert.IsType<SearchIndexProfileProvisioningService>(scopedServices.GetRequiredService<ISearchIndexProfileProvisioningService>());
    }
}
