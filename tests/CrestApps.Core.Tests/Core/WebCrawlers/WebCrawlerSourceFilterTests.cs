using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Sftp;
using CrestApps.Core.AI.WebCrawlers;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using Microsoft.Extensions.Localization;
using BlazorWebCrawlerSourceFilter = CrestApps.Core.Blazor.Web.Components.Pages.WebCrawlers.WebCrawlerSourceFilter;
using MvcWebCrawlerSourceFilter = CrestApps.Core.Mvc.Web.Areas.WebCrawlers.Services.WebCrawlerSourceFilter;

namespace CrestApps.Core.Tests.Core.WebCrawlers;

/// <summary>
/// Covers the one thing separating a web crawler from a file source: a record's source is either a crawl
/// strategy or an ingestion connector. Both kinds live in the same store, so the Web Crawlers screens have
/// to leave the connector-backed ones to the File Sources screens, in both hosts.
/// </summary>
public sealed class WebCrawlerSourceFilterTests
{
    [Fact]
    public void Mvc_SelectCrawlStrategyRecords_KeepsStrategiesAndDropsConnectors()
    {
        var records = new[]
        {
            CreateRecord("crawler-1", WebCrawlerConstants.Strategies.Sitemap),
            CreateRecord("file-source-1", FileSystemIngestionConnector.ConnectorName),
            CreateRecord("source-2", SftpIngestionConnector.ConnectorName),
        };

        var kept = MvcWebCrawlerSourceFilter.SelectCrawlStrategyRecords(records, CreateStrategies());

        Assert.Equal(["crawler-1"], kept.Select(record => record.ItemId));
    }

    [Fact]
    public void Blazor_SelectCrawlStrategyRecords_KeepsStrategiesAndDropsConnectors()
    {
        var records = new[]
        {
            CreateRecord("crawler-1", WebCrawlerConstants.Strategies.Sitemap),
            CreateRecord("file-source-1", FileSystemIngestionConnector.ConnectorName),
            CreateRecord("source-2", SftpIngestionConnector.ConnectorName),
        };

        var kept = BlazorWebCrawlerSourceFilter.SelectCrawlStrategyRecords(records, CreateStrategies());

        Assert.Equal(["crawler-1"], kept.Select(record => record.ItemId));
    }

    /// <summary>
    /// Verifies that the match ignores case, so a stored source that differs only in casing from what
    /// registered it still reaches its own screen.
    /// </summary>
    [Theory]
    [InlineData("Sitemap", true)]
    [InlineData("sitemap", true)]
    [InlineData(FileSystemIngestionConnector.ConnectorName, false)]
    [InlineData("SomethingNoLongerRegistered", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCrawlStrategy_AnswersForBothHosts(string source, bool expected)
    {
        var strategies = CreateStrategies();

        Assert.Equal(expected, MvcWebCrawlerSourceFilter.IsCrawlStrategy(source, strategies));
        Assert.Equal(expected, BlazorWebCrawlerSourceFilter.IsCrawlStrategy(source, strategies));
    }

    private static WebCrawler CreateRecord(string itemId, string source)
    {
        return new WebCrawler
        {
            ItemId = itemId,
            Source = source,
            DisplayText = itemId,
            AIDataSourceId = "data-source-1",
            Enabled = true,
        };
    }

    private static WebCrawlerStrategyDescriptor[] CreateStrategies()
    {
        return
        [
            new WebCrawlerStrategyDescriptor
            {
                Strategy = WebCrawlerConstants.Strategies.Sitemap,
                DisplayName = new LocalizedString("Sitemap", "Sitemap"),
                Description = new LocalizedString("Sitemap", "Discover pages through the site's sitemap."),
            },
        ];
    }
}
