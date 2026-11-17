using System.Text.Json.Nodes;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.WebCrawlers.Handlers;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;
using CrestApps.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.WebCrawlers;

/// <summary>
/// Covers what the crawler catalog handler accepts as a source and as a target. A record names either a crawl
/// strategy or an ingestion connector; a connector can only fill an Ingested data source, and a strategy can
/// fill a Web or an Ingested one.
/// </summary>
public sealed class WebCrawlerCatalogHandlerTests
{
    private const string WebDataSourceId = "web-ds";
    private const string IngestedDataSourceId = "ingested-ds";

    /// <summary>
    /// Verifies that a source nothing is registered under is still refused.
    /// </summary>
    [Fact]
    public async Task Validating_UnknownSource_IsRefused()
    {
        var context = CreateContext("Nothing", IngestedDataSourceId);

        await CreateHandler().ValidatingAsync(context, TestContext.Current.CancellationToken);

        Assert.False(context.Result.Succeeded);
        Assert.Contains(context.Result.Errors, error => error.MemberNames.Contains(nameof(WebCrawler.Source)));
    }

    /// <summary>
    /// Verifies that a crawl strategy may feed either kind of data source, which keeps every existing crawler
    /// valid and lets a site be crawled into typed knowledge.
    /// </summary>
    /// <param name="dataSourceId">The data source the crawler feeds.</param>
    [Theory]
    [InlineData(WebDataSourceId)]
    [InlineData(IngestedDataSourceId)]
    public async Task Validating_StrategyFeedingWebOrIngestedDataSource_IsAccepted(string dataSourceId)
    {
        var context = CreateContext("Sitemap", dataSourceId);

        await CreateHandler(strategy: Mock.Of<IWebCrawlerStrategy>()).ValidatingAsync(context, TestContext.Current.CancellationToken);

        Assert.True(context.Result.Succeeded);
    }

    /// <summary>
    /// Verifies that a data source that no longer exists is refused rather than silently accepted.
    /// </summary>
    [Fact]
    public async Task Validating_MissingDataSource_IsRefused()
    {
        var context = CreateContext("Sitemap", "gone");

        await CreateHandler(strategy: Mock.Of<IWebCrawlerStrategy>()).ValidatingAsync(context, TestContext.Current.CancellationToken);

        Assert.False(context.Result.Succeeded);
        Assert.Contains(context.Result.Errors, error => error.MemberNames.Contains(nameof(WebCrawler.AIDataSourceId)));
    }

    /// <summary>
    /// Verifies that a crawler created from JSON, as a recipe or a deployment import creates one, gets the
    /// strategy settings the JSON carries in <c>Properties</c>.
    /// </summary>
    [Fact]
    public async Task Initializing_WithProperties_AppliesTheStrategySettings()
    {
        var crawler = new WebCrawler();
        var data = JsonNode.Parse("""
            {
              "DisplayText": "Docs",
              "Source": "Sitemap",
              "Properties": {
                "SitemapWebCrawlerMetadata": {
                  "BaseUrl": "https://example.com",
                  "SitemapUrl": "https://example.com/sitemap.xml",
                  "MaxPages": 50
                }
              }
            }
            """);

        await CreateHandler().InitializingAsync(new InitializingContext<WebCrawler>(crawler, data), TestContext.Current.CancellationToken);

        Assert.True(crawler.TryGet<SitemapWebCrawlerMetadata>(out var metadata));
        Assert.Equal("https://example.com", metadata.BaseUrl);
        Assert.Equal("https://example.com/sitemap.xml", metadata.SitemapUrl);
        Assert.Equal(50, metadata.MaxPages);
    }

    /// <summary>
    /// Verifies that an update applies the settings it names and keeps the ones it leaves out, so a partial
    /// update does not erase the rest of the crawler's settings.
    /// </summary>
    [Fact]
    public async Task Updating_WithPartialProperties_MergesOverTheStoredSettings()
    {
        var crawler = new WebCrawler
        {
            ItemId = "crawler-1",
            DisplayText = "Docs",
            Source = "Sitemap",
        };

        crawler.Put(new SitemapWebCrawlerMetadata
        {
            BaseUrl = "https://old.example.com",
            SitemapUrl = "https://old.example.com/sitemap.xml",
            MaxPages = 50,
            IncludeUrlPatterns = ["/docs/*"],
        });

        var data = JsonNode.Parse("""
            {
              "Properties": {
                "SitemapWebCrawlerMetadata": {
                  "BaseUrl": "https://new.example.com",
                  "SitemapUrl": "https://new.example.com/sitemap.xml"
                }
              }
            }
            """);

        await CreateHandler().UpdatingAsync(new UpdatingContext<WebCrawler>(crawler, data), TestContext.Current.CancellationToken);

        Assert.True(crawler.TryGet<SitemapWebCrawlerMetadata>(out var metadata));
        Assert.Equal("https://new.example.com", metadata.BaseUrl);
        Assert.Equal("https://new.example.com/sitemap.xml", metadata.SitemapUrl);
        Assert.Equal(50, metadata.MaxPages);
        Assert.Equal(["/docs/*"], metadata.IncludeUrlPatterns);
    }

    private static ValidatingContext<WebCrawler> CreateContext(string source, string dataSourceId)
    {
        return new ValidatingContext<WebCrawler>(new WebCrawler
        {
            ItemId = "file-source-1",
            DisplayText = "The source",
            Source = source,
            AIDataSourceId = dataSourceId,
        });
    }

    private static WebCrawlerCatalogHandler CreateHandler(IWebCrawlerStrategy strategy = null)
    {
        var dataSources = new Dictionary<string, AIDataSource>(StringComparer.Ordinal)
        {
            [WebDataSourceId] = new AIDataSource { ItemId = WebDataSourceId, Source = AIDataSourceSourceTypes.Web },
            [IngestedDataSourceId] = new AIDataSource { ItemId = IngestedDataSourceId, Source = AIDataSourceSourceTypes.File },
        };

        var dataSourceStore = new Mock<IAIDataSourceStore>();
        dataSourceStore
            .Setup(store => store.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string id, CancellationToken _) => ValueTask.FromResult(dataSources.GetValueOrDefault(id)));

        var strategyResolver = new Mock<IWebCrawlerStrategyResolver>();
        strategyResolver
            .Setup(resolver => resolver.Get(It.IsAny<string>()))
            .Returns((string name) => name == "Sitemap" ? strategy : null);

        return new WebCrawlerCatalogHandler(
            Mock.Of<IHttpContextAccessor>(),
            TimeProvider.System,
            dataSourceStore.Object,
            Mock.Of<IAIDataSourceIndexingQueue>(),
            Mock.Of<IWebCrawlStateStore>(),
            strategyResolver.Object,
            NullLogger<WebCrawlerCatalogHandler>.Instance);
    }
}
