using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.WebCrawlers;
using CrestApps.Core.AI.WebCrawlers.Crawling;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;
using CrestApps.Core.DataIngestion;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestApps.Core.Tests.Core.WebCrawlers;

public sealed class WebCrawlerTests
{
    private const string Sitemap = "https://docs.example.com/sitemap.xml";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void HtmlReader_ExtractsTitleAndStripsScriptsStylesTags()
    {
        const string html = "<html><head><title>Hello &amp; Bye</title><style>a{}</style></head><body><script>x()</script><p>Body  text</p></body></html>";

        Assert.Equal("Hello & Bye", HtmlIngestionDocumentReader.ExtractTitle(html));

        var text = HtmlIngestionDocumentReader.ExtractText(html);
        Assert.Contains("Body text", text);
        Assert.DoesNotContain("x()", text);
        Assert.DoesNotContain("a{}", text);
    }

    [Fact]
    public void HtmlReader_ReadProducesIngestionDocumentWithContent()
    {
        var document = HtmlIngestionDocumentReader.Read("<html><body><p>Alpha</p><p>Beta</p></body></html>", "https://x.com/p");

        var text = string.Join(" ", document.EnumerateContent().Select(e => e.Text));
        Assert.Contains("Alpha", text);
        Assert.Contains("Beta", text);
    }

    [Fact]
    public async Task Crawler_ParsesUrlsetWithLastmod()
    {
        var routes = new Dictionary<string, (byte[] Body, string ContentType)>
        {
            [Sitemap] = (Xml(
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://docs.example.com/a</loc><lastmod>2024-01-02T00:00:00Z</lastmod></url>
                  <url><loc>https://docs.example.com/b</loc></url>
                </urlset>
                """), "application/xml"),
        };

        var crawler = new SitemapCrawler(NullLogger<SitemapCrawler>.Instance);
        using var client = new RoutingHttpClientFactory(routes).CreateClient("x");

        var entries = await crawler.DiscoverAsync(client, new SitemapCrawlRequest { SitemapUrl = Sitemap }, Ct);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero), entries.Single(e => e.Url.EndsWith("/a")).LastModifiedUtc);
    }

    [Fact]
    public async Task Crawler_FollowsSitemapIndex()
    {
        var routes = new Dictionary<string, (byte[] Body, string ContentType)>
        {
            [Sitemap] = (Xml(
                """
                <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <sitemap><loc>https://docs.example.com/child.xml</loc></sitemap>
                </sitemapindex>
                """), "application/xml"),
            ["https://docs.example.com/child.xml"] = (Xml(
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://docs.example.com/deep</loc></url>
                </urlset>
                """), "application/xml"),
        };

        var crawler = new SitemapCrawler(NullLogger<SitemapCrawler>.Instance);
        using var client = new RoutingHttpClientFactory(routes).CreateClient("x");

        var entries = await crawler.DiscoverAsync(client, new SitemapCrawlRequest { SitemapUrl = Sitemap }, Ct);

        Assert.Equal("https://docs.example.com/deep", Assert.Single(entries).Url);
    }

    [Fact]
    public async Task Strategy_DiscoverAppliesFilterAndFetchCleansPage()
    {
        var routes = new Dictionary<string, (byte[] Body, string ContentType)>
        {
            [Sitemap] = (Xml(
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://docs.example.com/keep</loc></url>
                  <url><loc>https://docs.example.com/drop</loc></url>
                </urlset>
                """), "application/xml"),
            ["https://docs.example.com/keep"] = (Html("Keep", "keep body"), "text/html"),
        };

        var strategy = CreateStrategy(routes);
        var crawler = CreateCrawler(exclude: ["/drop"]);

        var refs = await strategy.DiscoverAsync(crawler, Ct);
        Assert.Equal("https://docs.example.com/keep", Assert.Single(refs).Url);

        var page = await strategy.FetchAsync(crawler, "https://docs.example.com/keep", Ct);
        Assert.NotNull(page);
        Assert.Equal("Keep", page.Title);
        Assert.Contains("keep body", page.Content);
    }

    [Fact]
    public async Task Handler_ReadAsync_AggregatesCrawlersIntoOneDataSourceAndRecordsState()
    {
        var routes = new Dictionary<string, (byte[] Body, string ContentType)>
        {
            ["https://a.example.com/sitemap.xml"] = (Xml(SingleUrlSitemap("https://a.example.com/1", "2024-05-01T00:00:00Z")), "application/xml"),
            ["https://a.example.com/1"] = (Html("A1", "alpha body"), "text/html"),
            ["https://b.example.com/sitemap.xml"] = (Xml(SingleUrlSitemap("https://b.example.com/2", null)), "application/xml"),
            ["https://b.example.com/2"] = (Html("B2", "beta body"), "text/html"),
        };

        var crawlerStore = new InMemoryCrawlerStore();
        crawlerStore.Items.Add(CreateCrawler(itemId: "c1", sitemapUrl: "https://a.example.com/sitemap.xml"));
        crawlerStore.Items.Add(CreateCrawler(itemId: "c2", sitemapUrl: "https://b.example.com/sitemap.xml"));

        var stateStore = new InMemoryCrawlStateStore();
        var handler = new WebAIDataSourceSourceHandler(crawlerStore, stateStore, CreateResolver(routes), TimeProvider.System);

        var documents = await CollectAsync(handler.ReadAsync(CreateWebDataSource(), Ct));

        Assert.Equal(2, documents.Count);
        Assert.Contains(documents, d => d.Key == "https://a.example.com/1" && (string)d.Value.Fields[WebCrawlerConstants.UrlFieldName] == "https://a.example.com/1");
        Assert.Contains(documents, d => d.Key == "https://b.example.com/2");
        Assert.Equal(2, stateStore.Items.Count);
        Assert.Contains(stateStore.Items, s => s.Url == "https://a.example.com/1" && s.LastModifiedUtc == new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Handler_ReadByIdsAsync_FetchesRequestedUrlViaOwningCrawler()
    {
        var routes = new Dictionary<string, (byte[] Body, string ContentType)>
        {
            ["https://a.example.com/1"] = (Html("A1", "alpha body"), "text/html"),
        };

        var crawlerStore = new InMemoryCrawlerStore();
        crawlerStore.Items.Add(CreateCrawler(itemId: "c1", sitemapUrl: "https://a.example.com/sitemap.xml"));

        var stateStore = new InMemoryCrawlStateStore();
        stateStore.Items.Add(new WebCrawlState { ItemId = "s1", Source = "c1", Url = "https://a.example.com/1" });

        var handler = new WebAIDataSourceSourceHandler(crawlerStore, stateStore, CreateResolver(routes), TimeProvider.System);

        var documents = await CollectAsync(handler.ReadByIdsAsync(CreateWebDataSource(), ["https://a.example.com/1"], Ct));

        Assert.Equal("https://a.example.com/1", Assert.Single(documents).Key);
    }

    [Fact]
    public async Task Planner_DetectsNewChangedAndRemovedPages()
    {
        var resolver = new StubResolver(new StubStrategy(
        [
            new CrawledPageRef("https://x.com/new"),
            new CrawledPageRef("https://x.com/changed", new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero)),
            new CrawledPageRef("https://x.com/same", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        ]));

        var stateStore = new InMemoryCrawlStateStore();
        stateStore.Items.Add(new WebCrawlState { ItemId = "1", Source = "c1", Url = "https://x.com/changed", LastModifiedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        stateStore.Items.Add(new WebCrawlState { ItemId = "2", Source = "c1", Url = "https://x.com/same", LastModifiedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        stateStore.Items.Add(new WebCrawlState { ItemId = "3", Source = "c1", Url = "https://x.com/removed" });

        var queue = new RecordingIndexingQueue();
        var planner = new WebCrawlerReindexPlanner(resolver, stateStore, queue, TimeProvider.System, NullLogger<WebCrawlerReindexPlanner>.Instance);

        var result = await planner.PlanAndEnqueueAsync(CreateCrawler(itemId: "c1"), Ct);

        Assert.Equal(1, result.NewCount);
        Assert.Equal(1, result.ChangedCount);
        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Contains("https://x.com/new", queue.Synced);
        Assert.Contains("https://x.com/changed", queue.Synced);
        Assert.Contains("https://x.com/removed", queue.Removed);
        Assert.DoesNotContain(stateStore.Items, s => s.Url == "https://x.com/removed");
    }

    [Theory]
    [InlineData("https://x.com/page", "https://x.com/page")]
    [InlineData("not-a-url", null)]
    [InlineData("ftp://x.com/f", null)]
    public void LinkResolver_ReturnsUrlOnlyForHttpReferences(string referenceId, string expected)
    {
        Assert.Equal(expected, new WebCrawlerReferenceLinkResolver().ResolveLink(referenceId, null));
    }

    private static SitemapWebCrawlerStrategy CreateStrategy(IReadOnlyDictionary<string, (byte[] Body, string ContentType)> routes)
    {
        return new SitemapWebCrawlerStrategy(
            new SitemapCrawler(NullLogger<SitemapCrawler>.Instance),
            new RoutingHttpClientFactory(routes),
            Options.Create(new WebCrawlerOptions()),
            NullLogger<SitemapWebCrawlerStrategy>.Instance);
    }

    private static StubResolver CreateResolver(IReadOnlyDictionary<string, (byte[] Body, string ContentType)> routes)
    {
        return new StubResolver(CreateStrategy(routes));
    }

    private static WebCrawler CreateCrawler(string itemId = "c1", string sitemapUrl = null, List<string> exclude = null)
    {
        var crawler = new WebCrawler
        {
            ItemId = itemId,
            Source = WebCrawlerConstants.Strategies.Sitemap,
            AIDataSourceId = "ds1",
            Enabled = true,
        };
        crawler.Put(new SitemapWebCrawlerMetadata
        {
            SitemapUrl = sitemapUrl ?? Sitemap,
            ExcludeUrlPatterns = exclude,
        });

        return crawler;
    }

    private static AIDataSource CreateWebDataSource()
    {
        return new AIDataSource { ItemId = "ds1", Source = AIDataSourceSourceTypes.Web };
    }

    private static string SingleUrlSitemap(string url, string lastmod)
    {
        var lastmodElement = lastmod is null ? string.Empty : $"<lastmod>{lastmod}</lastmod>";

        return $"""
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>{url}</loc>{lastmodElement}</url>
            </urlset>
            """;
    }

    private static async Task<List<KeyValuePair<string, SourceDocument>>> CollectAsync(IAsyncEnumerable<KeyValuePair<string, SourceDocument>> source)
    {
        var items = new List<KeyValuePair<string, SourceDocument>>();

        await foreach (var item in source.WithCancellation(Ct))
        {
            items.Add(item);
        }

        return items;
    }

    private static byte[] Xml(string content)
    {
        return Encoding.UTF8.GetBytes(content);
    }

    private static byte[] Html(string title, string body)
    {
        return Encoding.UTF8.GetBytes($"<html><head><title>{title}</title></head><body>{body}</body></html>");
    }

    private sealed class StubResolver : IWebCrawlerStrategyResolver
    {
        private readonly IWebCrawlerStrategy _strategy;

        public StubResolver(IWebCrawlerStrategy strategy)
        {
            _strategy = strategy;
        }

        public IWebCrawlerStrategy Get(string strategy)
        {
            return string.Equals(strategy, _strategy.Name, StringComparison.OrdinalIgnoreCase) ? _strategy : null;
        }
    }

    private sealed class StubStrategy : IWebCrawlerStrategy
    {
        private readonly IReadOnlyList<CrawledPageRef> _refs;

        public StubStrategy(IReadOnlyList<CrawledPageRef> refs)
        {
            _refs = refs;
        }

        public string Name => WebCrawlerConstants.Strategies.Sitemap;

        public ValueTask ValidateAsync(WebCrawler crawler, ValidationResultDetails result, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public Task<IReadOnlyList<CrawledPageRef>> DiscoverAsync(WebCrawler crawler, CancellationToken cancellationToken = default) => Task.FromResult(_refs);

        public Task<CrawledPage> FetchAsync(WebCrawler crawler, string url, CancellationToken cancellationToken = default) => Task.FromResult(new CrawledPage("Title", "content"));
    }

    private sealed class RecordingIndexingQueue : IAIDataSourceIndexingQueue
    {
        public List<string> Synced { get; } = [];

        public List<string> Removed { get; } = [];

        public ValueTask QueueSyncDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask QueueDeleteDataSourceAsync(AIDataSource dataSource, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask QueueSyncSourceDocumentsAsync(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask QueueRemoveSourceDocumentsAsync(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask QueueSyncDataSourceDocumentsAsync(string dataSourceId, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
        {
            Synced.AddRange(documentIds);

            return ValueTask.CompletedTask;
        }

        public ValueTask QueueRemoveDataSourceDocumentsAsync(string dataSourceId, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
        {
            Removed.AddRange(documentIds);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class InMemoryCrawlerStore : InMemorySourceCatalog<WebCrawler>, IWebCrawlerStore
    {
        public Task<IReadOnlyCollection<WebCrawler>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<WebCrawler>>(Items.Where(x => x.AIDataSourceId == dataSourceId).ToArray());
        }
    }

    private sealed class InMemoryCrawlStateStore : InMemorySourceCatalog<WebCrawlState>, IWebCrawlStateStore
    {
        public Task DeleteByCrawlerIdAsync(string webCrawlerId, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(x => x.Source == webCrawlerId);

            return Task.CompletedTask;
        }

        public Task DeleteByUrlsAsync(string webCrawlerId, IEnumerable<string> urls, CancellationToken cancellationToken = default)
        {
            var set = new HashSet<string>(urls, StringComparer.OrdinalIgnoreCase);
            Items.RemoveAll(x => x.Source == webCrawlerId && set.Contains(x.Url));

            return Task.CompletedTask;
        }
    }

    private abstract class InMemorySourceCatalog<T> : ISourceCatalog<T>
        where T : CatalogItem, ISourceAwareModel
    {
        public List<T> Items { get; } = [];

        public ValueTask<IReadOnlyCollection<T>> GetAsync(string source, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyCollection<T>>(Items.Where(x => x.Source == source).ToArray());
        }

        public ValueTask CreateAsync(T entry, CancellationToken cancellationToken = default)
        {
            Items.Add(entry);

            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(T entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<bool> DeleteAsync(T entry, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Items.Remove(entry));
        }

        public ValueTask<T> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Items.FirstOrDefault(x => x.ItemId == id));
        }

        public ValueTask<IReadOnlyCollection<T>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyCollection<T>>(Items.ToArray());
        }

        public ValueTask<IReadOnlyCollection<T>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        {
            var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

            return ValueTask.FromResult<IReadOnlyCollection<T>>(Items.Where(x => set.Contains(x.ItemId)).ToArray());
        }

        public ValueTask<PageResult<T>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            return ValueTask.FromResult(new PageResult<T> { Count = Items.Count, Entries = Items.ToArray() });
        }
    }

    private sealed class RoutingHttpClientFactory : IHttpClientFactory
    {
        private readonly IReadOnlyDictionary<string, (byte[] Body, string ContentType)> _routes;

        public RoutingHttpClientFactory(IReadOnlyDictionary<string, (byte[] Body, string ContentType)> routes)
        {
            _routes = routes;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new RoutingHandler(_routes), disposeHandler: true);
        }

        private sealed class RoutingHandler : HttpMessageHandler
        {
            private readonly IReadOnlyDictionary<string, (byte[] Body, string ContentType)> _routes;

            public RoutingHandler(IReadOnlyDictionary<string, (byte[] Body, string ContentType)> routes)
            {
                _routes = routes;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = request.RequestUri.ToString();

                if (_routes.TryGetValue(url, out var route))
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(route.Body),
                    };
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue(route.ContentType);

                    return Task.FromResult(response);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }
}
