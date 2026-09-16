using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.WebCrawlers;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using CrestApps.Core.Models;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.WebCrawlers;

/// <summary>
/// Covers the rule that a page is removed only when the crawl that missed it actually covered the whole
/// site. A crawl that stopped short — a page limit, an unreachable sitemap — must never be read as a site
/// that shrank.
/// </summary>
public sealed class WebCrawlerCompletenessTests
{
    /// <summary>
    /// Verifies that a crawl which did not cover the whole site removes nothing, and says so.
    /// </summary>
    [Fact]
    public async Task Planner_PartialDiscovery_PerformsNoDeletionsAndRecordsIncomplete()
    {
        var stateStore = new InMemoryWebCrawlStateStore();

        await stateStore.CreateAsync(
            new WebCrawlState
            {
                ItemId = "1",
                Source = "c1",
                Url = "https://x.com/kept",
                LastIndexedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            TestContext.Current.CancellationToken);

        var queue = new RecordingIndexingQueue();
        var planner = CreatePlanner(
            new WebCrawlDiscovery(
                [new CrawledPageRef("https://x.com/other")],
                IsComplete: false,
                "The page limit was reached before the sitemap was fully read."),
            stateStore,
            queue);

        var result = await planner.PlanAndEnqueueAsync(CreateCrawler(), TestContext.Current.CancellationToken);

        Assert.Equal(WebCrawlerReindexStatus.PartiallyDiscovered, result.Status);
        Assert.Equal(0, result.RemovedCount);
        Assert.Empty(queue.Removed);

        // The page the crawl never reached is still tracked, so the next complete crawl decides its fate.
        Assert.Contains(stateStore.All, state => state.Url == "https://x.com/kept");
        Assert.Contains("page limit", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a crawl which did cover the whole site still removes what the site no longer has, so
    /// the gate narrows nothing that was working.
    /// </summary>
    [Fact]
    public async Task Planner_CompleteDiscovery_DeletesMissing()
    {
        var stateStore = new InMemoryWebCrawlStateStore();

        await stateStore.CreateAsync(
            new WebCrawlState
            {
                ItemId = "1",
                Source = "c1",
                Url = "https://x.com/gone",
                LastIndexedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            TestContext.Current.CancellationToken);

        var queue = new RecordingIndexingQueue();
        var planner = CreatePlanner(
            new WebCrawlDiscovery([new CrawledPageRef("https://x.com/kept")], IsComplete: true),
            stateStore,
            queue);

        var result = await planner.PlanAndEnqueueAsync(CreateCrawler(), TestContext.Current.CancellationToken);

        Assert.Equal(WebCrawlerReindexStatus.Completed, result.Status);
        Assert.Equal(1, result.RemovedCount);
        Assert.Contains("https://x.com/gone", queue.Removed);
        Assert.DoesNotContain(stateStore.All, state => state.Url == "https://x.com/gone");
    }

    private static WebCrawlerReindexPlanner CreatePlanner(
        WebCrawlDiscovery discovery,
        InMemoryWebCrawlStateStore stateStore,
        RecordingIndexingQueue queue)
    {
        return new WebCrawlerReindexPlanner(
            new StubResolver(new StubStrategy(discovery)),
            stateStore,
            queue,
            TimeProvider.System,
            NullLogger<WebCrawlerReindexPlanner>.Instance);
    }

    private static WebCrawler CreateCrawler()
    {
        return new WebCrawler
        {
            ItemId = "c1",
            Source = WebCrawlerConstants.Strategies.Sitemap,
            DisplayText = "The site",
            AIDataSourceId = "data-source-1",
            Enabled = true,
        };
    }

    private sealed class StubResolver : IWebCrawlerStrategyResolver
    {
        private readonly IWebCrawlerStrategy _strategy;

        public StubResolver(IWebCrawlerStrategy strategy)
        {
            _strategy = strategy;
        }

        public IWebCrawlerStrategy Get(string name)
        {
            return _strategy;
        }
    }

    /// <summary>
    /// Reports exactly the discovery a test handed it, complete or otherwise.
    /// </summary>
    private sealed class StubStrategy : IWebCrawlerStrategy
    {
        private readonly WebCrawlDiscovery _discovery;

        public StubStrategy(WebCrawlDiscovery discovery)
        {
            _discovery = discovery;
        }

        public string Name => WebCrawlerConstants.Strategies.Sitemap;

        public ValueTask ValidateAsync(WebCrawler crawler, ValidationResultDetails result, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public Task<IReadOnlyList<CrawledPageRef>> DiscoverAsync(WebCrawler crawler, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_discovery.Pages);
        }

        public Task<WebCrawlDiscovery> DiscoverDetailedAsync(WebCrawler crawler, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_discovery);
        }

        public Task<CrawledPage> FetchAsync(WebCrawler crawler, string url, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CrawledPage("Title", "content"));
        }
    }
}
