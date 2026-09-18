using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.WebCrawlers.Handlers;
using CrestApps.Core.AI.WebCrawlers.Strategies;
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
    /// Verifies that a record naming a connector that is not also a crawl strategy is accepted, and that the
    /// connector's own validation runs. Before this, every such indexer was refused as an unsupported
    /// strategy, so no folder or file-server indexer could be saved at all.
    /// </summary>
    [Fact]
    public async Task Validating_ConnectorSource_IsAcceptedAndValidatedByTheConnector()
    {
        var connector = new Mock<IIngestionConnector>();
        var context = CreateContext("FileSystem", IngestedDataSourceId);

        await CreateHandler(connector: connector.Object).ValidatingAsync(context, TestContext.Current.CancellationToken);

        Assert.True(context.Result.Succeeded);
        connector.Verify(
            instance => instance.ValidateAsync(context.Model, context.Result, It.IsAny<CancellationToken>()),
            Times.Once);
    }

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
    /// Verifies that a connector pointed at a Web data source is refused. The objects a connector produces
    /// are read back only by the Ingested source handler, so nothing would ever index them.
    /// </summary>
    [Fact]
    public async Task Validating_ConnectorFeedingWebDataSource_IsRefused()
    {
        var context = CreateContext("FileSystem", WebDataSourceId);

        await CreateHandler(connector: Mock.Of<IIngestionConnector>()).ValidatingAsync(context, TestContext.Current.CancellationToken);

        Assert.False(context.Result.Succeeded);
        Assert.Contains(context.Result.Errors, error => error.MemberNames.Contains(nameof(WebCrawler.AIDataSourceId)));
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

    private static ValidatingContext<WebCrawler> CreateContext(string source, string dataSourceId)
    {
        return new ValidatingContext<WebCrawler>(new WebCrawler
        {
            ItemId = "indexer-1",
            DisplayText = "The indexer",
            Source = source,
            AIDataSourceId = dataSourceId,
        });
    }

    private static WebCrawlerCatalogHandler CreateHandler(IWebCrawlerStrategy strategy = null, IIngestionConnector connector = null)
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

        var connectorResolver = new Mock<IIngestionConnectorResolver>();
        connectorResolver
            .Setup(resolver => resolver.Get(It.IsAny<string>()))
            .Returns((string name) => name == "FileSystem" ? connector : null);

        return new WebCrawlerCatalogHandler(
            Mock.Of<IHttpContextAccessor>(),
            TimeProvider.System,
            dataSourceStore.Object,
            Mock.Of<IAIDataSourceIndexingQueue>(),
            Mock.Of<IWebCrawlStateStore>(),
            strategyResolver.Object,
            connectorResolver.Object,
            NullLogger<WebCrawlerCatalogHandler>.Instance);
    }
}
