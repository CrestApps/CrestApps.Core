using CrestApps.Core.AI;
using CrestApps.Core.AI.Azure.AISearch;
using CrestApps.Core.AI.Elasticsearch;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIDataSourceServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCoreAIDataSourceRag_RegistersSharedSyncServices()
    {
        var services = new ServiceCollection();

        services.AddCoreAIDataSourceRag();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAIDataSourceIndexingQueue) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAIDataSourceIndexingService) &&
            descriptor.ImplementationType == typeof(DefaultAIDataSourceIndexingService) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICatalogEntryHandler<AIDataSource>) &&
            descriptor.ImplementationType == typeof(AIDataSourceCatalogHandler) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ISearchDocumentHandler) &&
            descriptor.ImplementationType == typeof(AIDataSourceSearchDocumentHandler) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(AIDataSourceIndexingBackgroundService) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(AIDataSourceAlignmentBackgroundService) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddCoreAzureAISearchAIDataSource_RegistersDataSourceSyncServices()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddCoreAzureAISearchAIDataSource();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAIDataSourceIndexingQueue) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ISearchDocumentHandler) &&
            descriptor.ImplementationType == typeof(AIDataSourceSearchDocumentHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IIndexProfileHandler) &&
            descriptor.ImplementationType == typeof(DataSourceSearchIndexProfileHandler));

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<IndexProfileSourceOptions>>().Value;

        Assert.Contains(options.Sources, source =>
            source.ProviderName == AISearchConstants.ProviderName &&
            source.Type == IndexProfileTypes.DataSource);
    }

    [Fact]
    public void AddCoreElasticsearchAIDataSource_RegistersDataSourceSyncServices()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddCoreElasticsearchAIDataSource();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAIDataSourceIndexingQueue) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ISearchDocumentHandler) &&
            descriptor.ImplementationType == typeof(AIDataSourceSearchDocumentHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IIndexProfileHandler) &&
            descriptor.ImplementationType == typeof(DataSourceSearchIndexProfileHandler));

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<IndexProfileSourceOptions>>().Value;

        Assert.Contains(options.Sources, source =>
            source.ProviderName == ElasticsearchConstants.ProviderName &&
            source.Type == IndexProfileTypes.DataSource);
    }
}
