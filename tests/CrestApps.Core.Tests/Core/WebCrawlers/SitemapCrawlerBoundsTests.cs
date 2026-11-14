using System.IO.Compression;
using System.Net;
using System.Text;
using CrestApps.Core.AI.Crawling;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.WebCrawlers;

public sealed class SitemapCrawlerBoundsTests
{
    private const string SitemapUrl = "https://docs.example.com/sitemap.xml";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Request_BoundsTheResponseByDefault()
    {
        var request = new SitemapCrawlRequest();

        Assert.True(request.MaxResponseBytes > 0);
        Assert.True(request.MaxDecompressedBytes >= request.MaxResponseBytes);
    }

    [Fact]
    public async Task Crawler_ReadsASitemapWithinTheLimits()
    {
        var handler = new StubHandler();
        handler.Add(SitemapUrl, UrlSet(3));
        var crawler = new SitemapCrawler(NullLogger<SitemapCrawler>.Instance);
        using var client = new HttpClient(handler);

        var result = await crawler.DiscoverDetailedAsync(client, new SitemapCrawlRequest { SitemapUrl = SitemapUrl }, Ct);

        Assert.Equal(3, result.Entries.Count);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task Crawler_DoesNotReadASitemapLargerThanTheResponseLimit()
    {
        var handler = new StubHandler();
        handler.Add(SitemapUrl, UrlSet(500));
        var crawler = new SitemapCrawler(NullLogger<SitemapCrawler>.Instance);
        using var client = new HttpClient(handler);

        var result = await crawler.DiscoverDetailedAsync(
            client,
            new SitemapCrawlRequest { SitemapUrl = SitemapUrl, MaxResponseBytes = 1024 },
            Ct);

        // Nothing is taken from a document that is too large, and the result says it is not the whole picture, so a
        // caller does not delete what it did not see.
        Assert.Empty(result.Entries);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task Crawler_DoesNotInflateAGzipSitemapPastTheDecompressedLimit()
    {
        // Small on the wire, large once inflated: the shape of a decompression bomb.
        var inflated = Encoding.UTF8.GetBytes(UrlSet(20000));
        var compressed = Gzip(inflated);
        Assert.True(compressed.Length < 64 * 1024, "the fixture is meant to be small on the wire");

        var handler = new StubHandler();
        handler.Add(SitemapUrl + ".gz", compressed);
        var crawler = new SitemapCrawler(NullLogger<SitemapCrawler>.Instance);
        using var client = new HttpClient(handler);

        var result = await crawler.DiscoverDetailedAsync(
            client,
            new SitemapCrawlRequest { SitemapUrl = SitemapUrl + ".gz", MaxDecompressedBytes = 16 * 1024 },
            Ct);

        Assert.Empty(result.Entries);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task Crawler_StillReadsAGzipSitemapWithinTheDecompressedLimit()
    {
        var handler = new StubHandler();
        handler.Add(SitemapUrl + ".gz", Gzip(Encoding.UTF8.GetBytes(UrlSet(5))));
        var crawler = new SitemapCrawler(NullLogger<SitemapCrawler>.Instance);
        using var client = new HttpClient(handler);

        var result = await crawler.DiscoverDetailedAsync(client, new SitemapCrawlRequest { SitemapUrl = SitemapUrl + ".gz" }, Ct);

        Assert.Equal(5, result.Entries.Count);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task Crawler_IgnoresARobotsFileLargerThanTheResponseLimit()
    {
        var handler = new StubHandler();
        var padding = new string('#', 4096);
        handler.Add("https://docs.example.com/robots.txt", $"{padding}\nSitemap: https://elsewhere.example.com/advertised.xml\n");
        handler.Add("https://docs.example.com/sitemap.xml", UrlSet(1));
        var crawler = new SitemapCrawler(NullLogger<SitemapCrawler>.Instance);
        using var client = new HttpClient(handler);

        var result = await crawler.DiscoverDetailedAsync(
            client,
            new SitemapCrawlRequest { BaseUrl = "https://docs.example.com", MaxResponseBytes = 2048 },
            Ct);

        Assert.DoesNotContain("https://elsewhere.example.com/advertised.xml", handler.Requested);
        Assert.Single(result.Entries);
    }

    private static string UrlSet(int count)
    {
        var builder = new StringBuilder("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        for (var i = 0; i < count; i++)
        {
            builder.Append("<url><loc>https://docs.example.com/page-").Append(i).Append("</loc></url>");
        }

        return builder.Append("</urlset>").ToString();
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _routes = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Requested { get; } = [];

        public void Add(string url, string body)
        {
            _routes[url] = Encoding.UTF8.GetBytes(body);
        }

        public void Add(string url, byte[] body)
        {
            _routes[url] = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            if (!_routes.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
        }
    }
}
