using System.Text;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.WebCrawlers.Connectors;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using CrestApps.Core.Models;

namespace CrestApps.Core.Tests.Core.WebCrawlers;

/// <summary>
/// Covers the adapter that presents a crawl strategy as an ingestion connector, so a website reaches the same
/// reader and store as a folder or a file server.
/// </summary>
public sealed class WebIngestionConnectorTests
{
    /// <summary>
    /// Verifies that a discovered page becomes an item whose identifier is its URL and whose change token
    /// comes from what the sitemap advertised.
    /// </summary>
    [Fact]
    public async Task DiscoverAsync_MapsRefsToItems()
    {
        var modified = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        var connector = new WebIngestionConnector(new FakeStrategy
        {
            Discovery = new WebCrawlDiscovery(
            [
                new CrawledPageRef("https://example.com/a", modified, "daily"),
                new CrawledPageRef("https://example.com/b"),
            ],
            IsComplete: true),
        });

        var result = await connector.DiscoverAsync(CreateCrawler(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(["https://example.com/a", "https://example.com/b"], result.Items.Select(item => item.ItemId));
        Assert.Equal(modified, result.Items[0].LastModifiedUtc);
        Assert.False(string.IsNullOrWhiteSpace(result.Items[0].ChangeToken));
        Assert.Null(result.Items[1].ChangeToken);
    }

    /// <summary>
    /// Verifies that a crawl which did not cover the whole site is reported incomplete, so nothing it failed
    /// to see is removed.
    /// </summary>
    [Fact]
    public async Task DiscoverAsync_PartialCrawl_IsReportedIncomplete()
    {
        var connector = new WebIngestionConnector(new FakeStrategy
        {
            Discovery = new WebCrawlDiscovery(
                [new CrawledPageRef("https://example.com/a")],
                IsComplete: false,
                "The page limit was reached."),
        });

        var result = await connector.DiscoverAsync(CreateCrawler(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Single(result.Items);
        Assert.Equal("The page limit was reached.", result.Message);
    }

    /// <summary>
    /// Verifies that a crawl returning nothing is never taken for a site with no pages.
    /// </summary>
    [Fact]
    public async Task DiscoverAsync_EmptyCrawl_IsReportedIncomplete()
    {
        var connector = new WebIngestionConnector(new FakeStrategy
        {
            Discovery = new WebCrawlDiscovery([], IsComplete: false),
        });

        var result = await connector.DiscoverAsync(CreateCrawler(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Items);
    }

    /// <summary>
    /// Verifies that a fetched page arrives as readable content with its title, declared as text so the
    /// resolver hands it to the plain-text reader.
    /// </summary>
    [Fact]
    public async Task FetchAsync_MapsPageToContent()
    {
        var connector = new WebIngestionConnector(new FakeStrategy
        {
            Page = new CrawledPage("The vacation policy", "Employees accrue 15 days per year."),
        });

        await using var content = await connector.FetchAsync(CreateCrawler(), "https://example.com/a", TestContext.Current.CancellationToken);

        Assert.NotNull(content);
        Assert.Equal("text/plain", content.MediaType);
        Assert.Equal("The vacation policy", content.Title);

        using var reader = new StreamReader(content.Content, Encoding.UTF8);

        Assert.Equal("Employees accrue 15 days per year.", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that a page with nothing on it is no content, rather than an empty document in the index.
    /// </summary>
    [Fact]
    public async Task FetchAsync_EmptyPage_ReturnsNothing()
    {
        var connector = new WebIngestionConnector(new FakeStrategy
        {
            Page = new CrawledPage("Title", "   "),
        });

        Assert.Null(await connector.FetchAsync(CreateCrawler(), "https://example.com/a", TestContext.Current.CancellationToken));
    }

    private static WebCrawler CreateCrawler()
    {
        return new WebCrawler
        {
            ItemId = "crawler-1",
            Source = "Sitemap",
            DisplayText = "The site",
            AIDataSourceId = "data-source-1",
            Enabled = true,
        };
    }

    private sealed class FakeStrategy : IWebCrawlerStrategy
    {
        public WebCrawlDiscovery Discovery { get; init; } = new([], IsComplete: false);

        public CrawledPage Page { get; init; }

        public string Name => "Sitemap";

        public ValueTask ValidateAsync(WebCrawler crawler, ValidationResultDetails result, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public Task<IReadOnlyList<CrawledPageRef>> DiscoverAsync(WebCrawler crawler, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Discovery.Pages);
        }

        public Task<WebCrawlDiscovery> DiscoverDetailedAsync(WebCrawler crawler, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Discovery);
        }

        public Task<CrawledPage> FetchAsync(WebCrawler crawler, string url, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Page);
        }
    }
}
