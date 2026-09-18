using System.Net;
using CrestApps.Core.AI.Crawling;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.WebCrawlers.Connectors;
using CrestApps.Core.AI.WebCrawlers.Handlers;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;
using CrestApps.Core.DataIngestion;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// Registration helpers for the web-crawlers feature.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the web-crawlers feature: the <c>Web</c> AI data source source handler and citation link
    /// resolver, the crawl strategies (starting with sitemap discovery), the re-index planner and its
    /// scheduled background service, the crawler catalog handler, and the shared crawling primitives. The
    /// <see cref="IWebCrawlerStore"/> and <see cref="IWebCrawlStateStore"/> are provided by the persistence
    /// layer (call the matching YesSql or EntityCore registration).
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreWebCrawlers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<WebCrawlerOptions>();
        services.AddOptions<WebCrawlerStrategyOptions>();
        services.AddCatalogManagers();
        services.AddCoreWebCrawlerCrawling();

        services
            .AddHttpClient(WebCrawlerConstants.HttpClientName, client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CrestApps-WebCrawler/1.0 (+https://crestapps.com)");
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // Transparently decompress sitemaps and pages and follow the redirects that sites such as
                // Yoast/Rank Math use for /sitemap.xml.
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = true,
            });

        services.TryAddSingleton<IWebCrawlerStrategyResolver, WebCrawlerStrategyResolver>();
        services.TryAddKeyedSingleton<IWebCrawlerStrategy, SitemapWebCrawlerStrategy>(WebCrawlerConstants.Strategies.Sitemap);
        services.Configure<WebCrawlerStrategyOptions>(options => options.AddOrUpdate(
            WebCrawlerConstants.Strategies.Sitemap,
            new LocalizedString("Sitemap", "Sitemap"),
            new LocalizedString("Sitemap Strategy Description", "Discover pages through the site's sitemap(s) and scrape each page as text.")));

        services.TryAddScoped<IWebCrawlerReindexPlanner, WebCrawlerReindexPlanner>();
        services.TryAddScoped<IWebCrawlerReindexService, WebCrawlerReindexService>();
        services.TryAddKeyedScoped<IAIDataSourceSourceHandler, WebAIDataSourceSourceHandler>(AIDataSourceSourceTypes.Web);
        services.TryAddKeyedSingleton<IAIReferenceLinkResolver, WebCrawlerReferenceLinkResolver>(AIDataSourceSourceTypes.Web);

        // A crawl strategy is an ingestion connector with Web in its name. Presenting it as one lets a
        // folder, a blob container and a website reach the same reader and the same store. Which path a
        // crawler record actually takes is decided by the data source it feeds: an Ingested data source is
        // filled through the indexer run service, a Web data source through the re-index planner.
        // Connectors are scoped, so the resolver has to be too: a singleton holding the root provider cannot
        // resolve a keyed scoped service and throws the first time an indexer runs.
        services.TryAddScoped<IIngestionConnectorResolver, KeyedIngestionConnectorResolver>();
        services.TryAddKeyedScoped<IIngestionConnector>(
            WebCrawlerConstants.Strategies.Sitemap,
            (sp, key) => new WebIngestionConnector(sp.GetRequiredKeyedService<IWebCrawlerStrategy>(key)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<WebCrawler>, WebCrawlerCatalogHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WebCrawlerReindexBackgroundService>());

        services.Configure<AIDataSourceSourceOptions>(options => options.AddOrUpdate(
            AIDataSourceSourceTypes.Web,
            new LocalizedString("Web", "Web"),
            new LocalizedString("Web Source Description", "A target for web crawlers. Configure the sites to scrape in the Web Crawlers area; each page is indexed with its URL kept for citations.")));

        return services;
    }

    /// <summary>
    /// Registers the shared sitemap crawler primitive. Page content is cleaned through the static
    /// <see cref="HtmlIngestionDocumentReader"/>. Registration is idempotent.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreWebCrawlerCrawling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISitemapCrawler, SitemapCrawler>();

        return services;
    }
}
