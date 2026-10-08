using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.WebCrawlers;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace CrestApps.Core.Tests.Core.WebCrawlers;

/// <summary>
/// Covers a host that registers the web crawlers feature without the file sources feature.
/// </summary>
/// <remarks>
/// The web crawlers feature registers the shared ingestion run service so a crawler that feeds an ingested
/// data source can be read. That service once needed a per-item state store only the file sources stores
/// registered, and knowledge ingestion only document ingestion registers. On a host without them the
/// container could not build the re-index service at all, so the scheduled re-index failed on every run
/// before reading a single crawler.
/// </remarks>
public sealed class WebCrawlerWithoutFileSourcesTests
{
    /// <summary>
    /// Verifies that the web crawler stores register the per-item state store on their own.
    /// </summary>
    [Fact]
    public void WebCrawlerStores_RegisterTheIngestionItemStateStore()
    {
        var entityCore = new ServiceCollection();
        entityCore.AddCoreWebCrawlerStoresEntityCore();

        var yesSql = new ServiceCollection();
        yesSql.AddCoreWebCrawlerStoresYesSql();

        Assert.Contains(entityCore, service => service.ServiceType == typeof(IIngestionItemStateStore));
        Assert.Contains(yesSql, service => service.ServiceType == typeof(IIngestionItemStateStore));
    }

    /// <summary>
    /// Verifies that a host enabling both features registers the per-item state store once.
    /// </summary>
    /// <remarks>
    /// Both features now call the same registration, so a second call must not add a second catalog that
    /// resolves the same store.
    /// </remarks>
    [Fact]
    public void BothFeatures_RegisterTheIngestionItemStateStoreOnce()
    {
        var entityCore = new ServiceCollection();
        entityCore.AddCoreWebCrawlerStoresEntityCore();
        entityCore.AddCoreFileSourceStoresEntityCore();

        var yesSql = new ServiceCollection();
        yesSql.AddCoreWebCrawlerStoresYesSql();
        yesSql.AddCoreFileSourceStoresYesSql();

        foreach (var services in new[] { entityCore, yesSql })
        {
            Assert.Single(services, service => service.ServiceType == typeof(IIngestionItemStateStore));
            Assert.Single(services, service => service.ServiceType == typeof(ICatalog<IngestionItemState>));
            Assert.Single(services, service => service.ServiceType == typeof(ISourceCatalog<IngestionItemState>));
        }
    }

    /// <summary>
    /// Verifies that the re-index service can be built without document ingestion, and that no run service
    /// is offered then.
    /// </summary>
    [Fact]
    public void WithoutDocumentIngestion_TheReindexServiceResolvesWithoutARunService()
    {
        using var provider = BuildWebCrawlersHost(withDocumentIngestion: false);
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IWebCrawlerReindexService>());
        Assert.Null(scope.ServiceProvider.GetService<IIngestionRunService>());
    }

    /// <summary>
    /// Verifies that a host with document ingestion but no file sources gets the real run service, so its
    /// crawlers that feed an ingested data source are read.
    /// </summary>
    [Fact]
    public void WithDocumentIngestion_TheRunServiceIsBuiltWithoutFileSources()
    {
        using var provider = BuildWebCrawlersHost(withDocumentIngestion: true);
        using var scope = provider.CreateScope();

        Assert.IsType<DefaultIngestionRunService>(scope.ServiceProvider.GetService<IIngestionRunService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IWebCrawlerReindexService>());
    }

    private static ServiceProvider BuildWebCrawlersHost(bool withDocumentIngestion)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddCoreWebCrawlers();
        services.AddCoreWebCrawlerStoresEntityCore();

        // The stores need a database a bare container has not got; which services are offered is the question.
        services.Replace(ServiceDescriptor.Scoped(_ => Mock.Of<IWebCrawlerStore>()));
        services.Replace(ServiceDescriptor.Scoped(_ => Mock.Of<IWebCrawlStateStore>()));
        services.Replace(ServiceDescriptor.Scoped(_ => Mock.Of<IIngestionItemStateStore>()));
        services.Replace(ServiceDescriptor.Scoped(_ => Mock.Of<IWebCrawlerReindexPlanner>()));
        services.AddScoped(_ => Mock.Of<IAIDataSourceStore>());

        if (withDocumentIngestion)
        {
            services.AddScoped(_ => Mock.Of<IKnowledgeIngestionService>());
        }

        return services.BuildServiceProvider();
    }
}
