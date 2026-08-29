using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Crawling;
using CrestApps.Core.AI.Tooling.Instances.Documentation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Tools;

public sealed class DocumentationSearchTests
{
    /// <summary>
    /// Verifies that the sitemap source materializes a documentation search function whose model-facing
    /// name and description are derived from the configured instance.
    /// </summary>
    [Fact]
    public void SitemapSource_CreateTool_DerivesNameAndDescriptionFromInstance()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Source = DocumentationToolConstants.SitemapSourceName,
            Name = "crestapps-docs",
            Description = "Searches the CrestApps documentation.",
        };

        instance.Put(new SitemapDocumentationToolSettings
        {
            BaseUrl = "https://core.crestapps.com",
        });

        var source = new SitemapDocumentationToolSource();
        var tool = source.CreateTool(instance);

        var function = Assert.IsType<DocumentationSearchToolFunction>(tool);

        Assert.Equal(instance.GetFunctionName(), function.Name);
        Assert.Equal("Searches the CrestApps documentation.", function.Description);
    }

    /// <summary>
    /// Verifies that the search-index source produces a documentation search function.
    /// </summary>
    [Fact]
    public void SearchIndexSource_CreateTool_ProducesFunction()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-2",
            Source = DocumentationToolConstants.SearchIndexSourceName,
            Name = "mkdocs",
        };

        instance.Put(new SearchIndexDocumentationToolSettings
        {
            BaseUrl = "https://docs.example.com",
            IndexUrl = "https://docs.example.com/search/search_index.json",
        });

        var source = new SearchIndexDocumentationToolSource();
        var tool = source.CreateTool(instance);

        Assert.IsType<DocumentationSearchToolFunction>(tool);
    }

    /// <summary>
    /// Verifies that the Algolia source produces a documentation search function.
    /// </summary>
    [Fact]
    public void AlgoliaSource_CreateTool_ProducesFunction()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-3",
            Source = DocumentationToolConstants.AlgoliaSourceName,
            Name = "algolia",
        };

        instance.Put(new AlgoliaDocumentationToolSettings
        {
            ApplicationId = "APP123",
            ApiKey = "search-key",
            IndexName = "docs-index",
        });

        var source = new AlgoliaDocumentationToolSource();
        var tool = source.CreateTool(instance);

        Assert.IsType<DocumentationSearchToolFunction>(tool);
    }

    /// <summary>
    /// Verifies that the Algolia source unprotects the stored API key before issuing a search request.
    /// </summary>
    [Fact]
    public async Task AlgoliaSource_InvokeAsync_UnprotectsStoredApiKey()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var protector = dataProtectionProvider.CreateProtector(DocumentationToolConstants.AlgoliaDataProtectionPurpose);
        var httpClientFactory = new CapturingHttpClientFactory();
        var instance = new AIToolInstance
        {
            ItemId = "instance-4",
            Source = DocumentationToolConstants.AlgoliaSourceName,
            Name = "algolia",
            CreatedUtc = DateTime.UnixEpoch,
        };

        instance.Put(new AlgoliaDocumentationToolSettings
        {
            ApplicationId = "APP123",
            ApiKey = protector.Protect("search-key"),
            IndexName = "docs-index",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(dataProtectionProvider);
        services.AddSingleton<IDocumentationSourceMaterializer, DefaultDocumentationSourceMaterializer>();
        services.AddSingleton<IHttpClientFactory>(httpClientFactory);

        using var provider = services.BuildServiceProvider();

        var source = new AlgoliaDocumentationToolSource();
        var function = Assert.IsType<DocumentationSearchToolFunction>(source.CreateTool(instance));
        var arguments = new AIFunctionArguments
        {
            Services = provider,
        };
        arguments["query"] = "start";

        var result = await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        Assert.Contains("Getting started", result?.ToString());
        Assert.Equal("search-key", httpClientFactory.ApiKey);
    }

    /// <summary>
    /// Verifies that invoking the function returns a helpful message when the required query is missing.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenQueryMissing_ReturnsMessage()
    {
        var function = CreateFunction(new FakeDocumentationSource("docs"));

        var result = await InvokeAsync(function, query: null);

        Assert.Contains("query", result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that invoking the function returns a message when the source yields no results.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenNoResults_ReturnsMessage()
    {
        var function = CreateFunction(new FakeDocumentationSource("docs"));

        var result = await InvokeAsync(function, "anything");

        Assert.Contains("No results were found for 'anything'.", result);

        // The message offers a bounded retry then a clear stop, so a fruitless search does not loop.
        Assert.Contains("stop searching", result);
    }

    /// <summary>
    /// Verifies that invoking the function formats the results returned by the bound source.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_FormatsResults()
    {
        var source = new FakeDocumentationSource(
            "docs",
            new DocumentationSearchResult
            {
                SourceName = "docs",
                Title = "Getting started",
                Url = "https://core.crestapps.com/start",
                Snippet = "How to begin.",
                Score = 5,
            });

        var function = CreateFunction(source);

        var result = await InvokeAsync(function, "start");

        Assert.Contains("[1] Getting started — https://core.crestapps.com/start", result);
        Assert.Contains("How to begin.", result);
    }

    /// <summary>
    /// Verifies that the materializer caches the built source until the signature changes.
    /// </summary>
    [Fact]
    public void Materializer_CachesUntilSignatureChanges()
    {
        var materializer = new DefaultDocumentationSourceMaterializer();
        var buildCount = 0;

        IDocumentationSource Factory()
        {
            buildCount++;

            return new FakeDocumentationSource("docs");
        }

        var first = materializer.GetOrCreate("key", "sig-1", Factory);
        var second = materializer.GetOrCreate("key", "sig-1", Factory);

        Assert.Same(first, second);
        Assert.Equal(1, buildCount);

        var third = materializer.GetOrCreate("key", "sig-2", Factory);

        Assert.NotSame(first, third);
        Assert.Equal(2, buildCount);
    }

    /// <summary>
    /// Verifies that the sitemap crawler follows a sitemap index into its child sitemaps, decompresses a
    /// gzip-compressed child sitemap, ignores non-page <c>&lt;image:loc&gt;</c> asset URLs, and indexes the
    /// discovered pages so they become searchable.
    /// </summary>
    [Fact]
    public async Task SitemapSource_Search_FollowsIndex_Decompresses_AndSkipsAssetUrls()
    {
        const string indexXml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <sitemap><loc>https://site.test/page-sitemap.xml</loc></sitemap>
              <sitemap><loc>https://site.test/gz-sitemap.xml.gz</loc></sitemap>
            </sitemapindex>
            """;

        const string pageSitemapXml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:image="http://www.google.com/schemas/sitemap-image/1.1">
              <url>
                <loc>https://site.test/tickets</loc>
                <image:image><image:loc>https://site.test/poster.jpg</image:loc></image:image>
              </url>
            </urlset>
            """;

        const string gzSitemapXml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>https://site.test/location</loc></url>
            </urlset>
            """;

        var routes = new Dictionary<string, (byte[] Body, string ContentType)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://site.test/sitemap_index.xml"] = (Encoding.UTF8.GetBytes(indexXml), "text/xml"),
            ["https://site.test/page-sitemap.xml"] = (Encoding.UTF8.GetBytes(pageSitemapXml), "text/xml"),
            ["https://site.test/gz-sitemap.xml.gz"] = (Gzip(gzSitemapXml), "application/gzip"),
            ["https://site.test/tickets"] = (Html("Tickets", "Buy theater tickets and showtimes here."), "text/html"),
            ["https://site.test/location"] = (Html("Location", "The theater is located downtown."), "text/html"),
        };

        var factory = new RoutingHttpClientFactory(routes);
        var source = CreateSitemapSource(factory, sitemapUrl: "https://site.test/sitemap_index.xml");

        var results = await source.SearchAsync(new DocumentationSearchRequest("theater"), TestContext.Current.CancellationToken);

        var urls = results.Select(result => result.Url).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains("https://site.test/tickets", urls);
        Assert.Contains("https://site.test/location", urls);

        // The image asset URL must never be treated as a page and fetched.
        Assert.DoesNotContain("https://site.test/poster.jpg", factory.RequestedUrls);
    }

    /// <summary>
    /// Verifies that the crawler supports a plain-text sitemap that lists one absolute page URL per line.
    /// </summary>
    [Fact]
    public async Task SitemapSource_Search_SupportsPlainTextSitemap()
    {
        var sitemapText = string.Join('\n', "https://site.test/a", "https://site.test/b");

        var routes = new Dictionary<string, (byte[] Body, string ContentType)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://site.test/sitemap.txt"] = (Encoding.UTF8.GetBytes(sitemapText), "text/plain"),
            ["https://site.test/a"] = (Html("A", "First theater page."), "text/html"),
            ["https://site.test/b"] = (Html("B", "Second theater page."), "text/html"),
        };

        var factory = new RoutingHttpClientFactory(routes);
        var source = CreateSitemapSource(factory, sitemapUrl: "https://site.test/sitemap.txt");

        var results = await source.SearchAsync(new DocumentationSearchRequest("theater"), TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
    }

    /// <summary>
    /// Verifies that when no explicit sitemap URL is configured the crawler discovers the sitemap from the
    /// site's <c>robots.txt</c> <c>Sitemap:</c> directive.
    /// </summary>
    [Fact]
    public async Task SitemapSource_Search_DiscoversSitemapFromRobots()
    {
        const string urlsetXml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>https://site.test/shows</loc></url>
            </urlset>
            """;

        var routes = new Dictionary<string, (byte[] Body, string ContentType)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://site.test/robots.txt"] = (Encoding.UTF8.GetBytes("User-agent: *\nSitemap: https://site.test/custom-sitemap.xml"), "text/plain"),
            ["https://site.test/custom-sitemap.xml"] = (Encoding.UTF8.GetBytes(urlsetXml), "text/xml"),
            ["https://site.test/shows"] = (Html("Shows", "Upcoming theater shows."), "text/html"),
        };

        var factory = new RoutingHttpClientFactory(routes);
        var source = CreateSitemapSource(factory, baseUrl: "https://site.test");

        var results = await source.SearchAsync(new DocumentationSearchRequest("theater"), TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("https://site.test/shows", results[0].Url);
    }

    /// <summary>
    /// Verifies that the crawler treats an RSS 2.0 feed as a sitemap, indexing each item's link while
    /// ignoring the channel-level homepage link.
    /// </summary>
    [Fact]
    public async Task SitemapSource_Search_SupportsRssFeed()
    {
        const string rss =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0">
              <channel>
                <title>Theater</title>
                <link>https://site.test</link>
                <item><title>One</title><link>https://site.test/one</link></item>
                <item><title>Two</title><link>https://site.test/two</link></item>
              </channel>
            </rss>
            """;

        var routes = new Dictionary<string, (byte[] Body, string ContentType)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://site.test/feed.xml"] = (Encoding.UTF8.GetBytes(rss), "application/rss+xml"),
            ["https://site.test/one"] = (Html("One", "First theater article."), "text/html"),
            ["https://site.test/two"] = (Html("Two", "Second theater article."), "text/html"),
        };

        var factory = new RoutingHttpClientFactory(routes);
        var source = CreateSitemapSource(factory, sitemapUrl: "https://site.test/feed.xml");

        var results = await source.SearchAsync(new DocumentationSearchRequest("theater"), TestContext.Current.CancellationToken);

        var urls = results.Select(result => result.Url).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains("https://site.test/one", urls);
        Assert.Contains("https://site.test/two", urls);

        // The channel-level homepage link must not be crawled as a page.
        Assert.DoesNotContain("https://site.test", factory.RequestedUrls);
    }

    /// <summary>
    /// Verifies that the crawler treats an Atom 1.0 feed as a sitemap, reading each entry's alternate link
    /// href while ignoring the self relation.
    /// </summary>
    [Fact]
    public async Task SitemapSource_Search_SupportsAtomFeed()
    {
        const string atom =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Theater</title>
              <link rel="self" href="https://site.test/atom.xml"/>
              <entry>
                <title>Show</title>
                <link rel="alternate" href="https://site.test/show"/>
                <link rel="edit" href="https://site.test/edit/show"/>
              </entry>
            </feed>
            """;

        var routes = new Dictionary<string, (byte[] Body, string ContentType)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://site.test/atom.xml"] = (Encoding.UTF8.GetBytes(atom), "application/atom+xml"),
            ["https://site.test/show"] = (Html("Show", "A theater show."), "text/html"),
        };

        var factory = new RoutingHttpClientFactory(routes);
        var source = CreateSitemapSource(factory, sitemapUrl: "https://site.test/atom.xml");

        var results = await source.SearchAsync(new DocumentationSearchRequest("theater"), TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("https://site.test/show", results[0].Url);
        Assert.DoesNotContain("https://site.test/edit/show", factory.RequestedUrls);
    }

    /// <summary>
    /// Verifies that a sentence-style query ranks on its meaningful terms: common function words such as
    /// "what", "is", and "the" are ignored so a page that only matches those words is not returned.
    /// </summary>
    [Fact]
    public void Corpus_Search_IgnoresStopWordsInQuery()
    {
        var corpus = new DocumentationCorpus(
        [
            new DocumentationCorpus.Entry("https://site.test/tickets", "Tickets", "Buy theater tickets here."),
            new DocumentationCorpus.Entry("https://site.test/about", "About", "This is the about page for the company."),
        ]);

        var results = corpus.Search("what is the theater", "docs", 5);

        Assert.Single(results);
        Assert.Equal("https://site.test/tickets", results[0].Url);
    }

    /// <summary>
    /// Verifies that a page containing the query as an exact phrase outranks a page that mentions the same
    /// keywords more often but scattered apart — even though the scattered page has the higher raw term count.
    /// </summary>
    [Fact]
    public void Corpus_Search_RanksExactPhraseAboveScatteredKeywords()
    {
        var corpus = new DocumentationCorpus(
        [
            new DocumentationCorpus.Entry("https://site.test/phrase", "A", "box office"),
            new DocumentationCorpus.Entry("https://site.test/scattered", "B", "box. office. box. office."),
        ]);

        var results = corpus.Search("box office", "docs", 5);

        Assert.Equal(2, results.Count);

        // Without the phrase bonus the scattered page (higher raw term count) would rank first.
        Assert.Equal("https://site.test/phrase", results[0].Url);
    }

    /// <summary>
    /// Verifies that a source whose corpus builds within the wait budget returns results on the very first
    /// search.
    /// </summary>
    [Fact]
    public async Task CachingSource_WhenBuildWithinBudget_ReturnsResultsOnFirstSearch()
    {
        var corpus = new DocumentationCorpus([new DocumentationCorpus.Entry("https://s/1", "T", "theater tickets")]);
        var source = new SlowCorpusSource(TimeSpan.Zero, TimeSpan.FromSeconds(5), corpus);

        var results = await source.SearchAsync(new DocumentationSearchRequest("theater"), TestContext.Current.CancellationToken);

        Assert.Single(results);
    }

    /// <summary>
    /// Verifies that a slow first build does not block the caller: the search reports the index as pending
    /// while the corpus keeps building in the background, and a later search serves results from it without
    /// rebuilding.
    /// </summary>
    [Fact]
    public async Task CachingSource_WhenBuildExceedsBudget_ReportsPendingThenServesFromBackgroundBuild()
    {
        var corpus = new DocumentationCorpus([new DocumentationCorpus.Entry("https://s/1", "T", "theater tickets")]);
        var source = new SlowCorpusSource(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(20), corpus);

        // The first search cannot beat the 20ms budget, so it reports the index as still pending.
        await Assert.ThrowsAsync<DocumentationIndexPendingException>(
            () => source.SearchAsync(new DocumentationSearchRequest("theater"), TestContext.Current.CancellationToken));

        // The background build keeps running; a later search returns results and never rebuilds.
        IReadOnlyList<DocumentationSearchResult> results = [];

        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                results = await source.SearchAsync(new DocumentationSearchRequest("theater"), TestContext.Current.CancellationToken);

                break;
            }
            catch (DocumentationIndexPendingException)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        Assert.Single(results);
        Assert.Equal(1, source.BuildCount);
    }

    /// <summary>
    /// Verifies that the website search source queries the WordPress REST search endpoint and maps each
    /// hit's title, URL, and embedded excerpt (with HTML stripped) into a documentation result.
    /// </summary>
    [Fact]
    public async Task WebsiteSearch_MapsWordPressResponse_WithEmbeddedExcerpt()
    {
        const string json =
            """
            [
              {
                "id": 1,
                "title": "About the 20th Century Theater",
                "url": "https://site.test/about/",
                "type": "post",
                "subtype": "page",
                "_embedded": {
                  "self": [
                    { "excerpt": { "rendered": "<p>A historic <b>theater</b> venue.</p>" } }
                  ]
                }
              }
            ]
            """;

        var routes = new Dictionary<string, (byte[] Body, string ContentType)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://site.test/wp-json/wp/v2/search?search=theater&_embed=1"] = (Encoding.UTF8.GetBytes(json), "application/json"),
        };

        var source = CreateWebsiteSearchSource(new RoutingHttpClientFactory(routes), new WebsiteSearchSite
        {
            Name = "theater",
            BaseUrl = "https://site.test",
            SearchPath = "/wp-json/wp/v2/search",
            QueryParameter = "search",
            ExtraQuery = "_embed=1",
            TitlePath = "title",
            UrlPath = "url",
            SnippetPath = "_embedded.self[0].excerpt.rendered",
        });

        var results = await source.SearchAsync(new DocumentationSearchRequest("theater"), TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("About the 20th Century Theater", results[0].Title);
        Assert.Equal("https://site.test/about/", results[0].Url);
        Assert.Equal("A historic theater venue.", results[0].Snippet);
    }

    /// <summary>
    /// Verifies that the field mappings can target a non-WordPress response whose results are nested under
    /// a property and whose title/url fields are named differently.
    /// </summary>
    [Fact]
    public async Task WebsiteSearch_MapsCustomResponseShape()
    {
        const string json =
            """
            { "results": [ { "name": "Doc A", "link": "https://site.test/a" } ] }
            """;

        var routes = new Dictionary<string, (byte[] Body, string ContentType)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://site.test/search?q=hello"] = (Encoding.UTF8.GetBytes(json), "application/json"),
        };

        var source = CreateWebsiteSearchSource(new RoutingHttpClientFactory(routes), new WebsiteSearchSite
        {
            Name = "custom",
            BaseUrl = "https://site.test",
            SearchPath = "/search",
            QueryParameter = "q",
            ResultsPath = "results",
            TitlePath = "name",
            UrlPath = "link",
            SnippetPath = null,
        });

        var results = await source.SearchAsync(new DocumentationSearchRequest("hello"), TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Doc A", results[0].Title);
        Assert.Equal("https://site.test/a", results[0].Url);
    }

    private static WebsiteSearchSource CreateWebsiteSearchSource(IHttpClientFactory httpClientFactory, WebsiteSearchSite site)
    {
        return new WebsiteSearchSource(
            site,
            new DocumentationSearchOptions(),
            httpClientFactory,
            NullLogger<WebsiteSearchSource>.Instance);
    }

    private static SitemapDocumentationSource CreateSitemapSource(
        IHttpClientFactory httpClientFactory,
        string sitemapUrl = null,
        string baseUrl = null)
    {
        var site = new DocumentationSite
        {
            Name = "theater",
            BaseUrl = baseUrl,
            SitemapUrl = sitemapUrl,
        };

        return new SitemapDocumentationSource(
            site,
            new DocumentationSearchOptions(),
            httpClientFactory,
            new SitemapCrawler(NullLogger<SitemapCrawler>.Instance),
            TimeProvider.System,
            NullLogger<SitemapDocumentationSource>.Instance);
    }

    private static byte[] Html(string title, string body)
    {
        return Encoding.UTF8.GetBytes($"<html><head><title>{title}</title></head><body>{body}</body></html>");
    }

    private static byte[] Gzip(string content)
    {
        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Verifies that a live search source, when it finds nothing, tells the model it may retry with
    /// simpler, corrected keywords (because a reworded live query can return results) rather than stopping.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenNoResults_LiveSource_EncouragesCorrectedRetry()
    {
        var function = CreateFunction(new FakeDocumentationSource("docs"), isLiveSearch: true);

        var result = await InvokeAsync(function, "anything");

        Assert.Contains("live search", result);
        Assert.Contains("key nouns", result);
    }

    private static DocumentationSearchToolFunction CreateFunction(IDocumentationSource source, bool isLiveSearch = false)
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Name = "docs",
            CreatedUtc = DateTime.UnixEpoch,
        };

        return new DocumentationSearchToolFunction("docs", "Docs search", instance, _ => source, isLiveSearch);
    }

    private static async Task<string> InvokeAsync(DocumentationSearchToolFunction function, string query)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDocumentationSourceMaterializer, DefaultDocumentationSourceMaterializer>();

        using var provider = services.BuildServiceProvider();

        var arguments = new AIFunctionArguments
        {
            Services = provider,
        };

        if (query is not null)
        {
            arguments["query"] = query;
        }

        var result = await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        return result?.ToString();
    }

    private sealed class FakeDocumentationSource : IDocumentationSource
    {
        private readonly DocumentationSearchResult[] _results;

        public FakeDocumentationSource(string name, params DocumentationSearchResult[] results)
        {
            Name = name;
            _results = results;
        }

        public string Name { get; }

        public Task<IReadOnlyList<DocumentationSearchResult>> SearchAsync(DocumentationSearchRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DocumentationSearchResult>>(_results);
        }
    }

    private sealed class SlowCorpusSource : CachingDocumentationSource
    {
        private readonly TimeSpan _delay;
        private readonly DocumentationCorpus _corpus;
        private int _buildCount;

        public SlowCorpusSource(TimeSpan delay, TimeSpan budget, DocumentationCorpus corpus)
            : base("slow", TimeSpan.FromHours(1), TimeProvider.System, budget)
        {
            _delay = delay;
            _corpus = corpus;
        }

        public int BuildCount => _buildCount;

        protected override int MaxResults => 5;

        protected override async Task<DocumentationCorpus> BuildCorpusAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _buildCount);

            await Task.Delay(_delay, cancellationToken);

            return _corpus;
        }
    }

    private sealed class RoutingHttpClientFactory : IHttpClientFactory
    {
        private readonly IReadOnlyDictionary<string, (byte[] Body, string ContentType)> _routes;

        public RoutingHttpClientFactory(IReadOnlyDictionary<string, (byte[] Body, string ContentType)> routes)
        {
            _routes = routes;
        }

        public List<string> RequestedUrls { get; } = [];

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new RoutingHandler(this), disposeHandler: true);
        }

        private sealed class RoutingHandler : HttpMessageHandler
        {
            private readonly RoutingHttpClientFactory _factory;

            public RoutingHandler(RoutingHttpClientFactory factory)
            {
                _factory = factory;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = request.RequestUri.ToString();
                _factory.RequestedUrls.Add(url);

                if (_factory._routes.TryGetValue(url, out var route))
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

    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        private const string _responsePayload =
            """
            {
              "hits": [
                {
                  "url": "https://docs.example.com/start",
                  "content": "Start here.",
                  "hierarchy": {
                    "lvl0": "Getting started"
                  }
                }
              ]
            }
            """;

        public string ApiKey { get; private set; }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new CapturingHandler(this), disposeHandler: true);
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            private readonly CapturingHttpClientFactory _factory;

            public CapturingHandler(CapturingHttpClientFactory factory)
            {
                _factory = factory;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Headers.TryGetValues("X-Algolia-API-Key", out var values))
                {
                    _factory.ApiKey = values.SingleOrDefault();
                }

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responsePayload, Encoding.UTF8, "application/json"),
                };

                return Task.FromResult(response);
            }
        }
    }
}
