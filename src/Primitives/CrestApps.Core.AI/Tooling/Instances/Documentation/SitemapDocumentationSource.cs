using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// A built-in <see cref="IDocumentationSource"/> that indexes a public documentation site by reading
/// its sitemap, fetching each page, and ranking pages against a query using lightweight keyword scoring.
/// The crawler understands the full sitemaps.org protocol as it appears on the market: a flat
/// <c>&lt;urlset&gt;</c>, a <c>&lt;sitemapindex&gt;</c> that nests child sitemaps (for example those
/// emitted by Yoast, Rank Math, or Google), gzip-compressed sitemaps (<c>.xml.gz</c>), plain-text
/// sitemaps, and sitemaps advertised through <c>robots.txt</c>. The crawled corpus is cached in memory
/// and refreshed based on <see cref="DocumentationSearchOptions.CacheDuration"/>.
/// </summary>
public sealed partial class SitemapDocumentationSource : CachingDocumentationSource
{
    /// <summary>
    /// The maximum number of nested sitemap levels the crawler follows. A sitemap index may point at
    /// child indexes; this bounds that recursion so a misconfigured or hostile site cannot loop forever.
    /// </summary>
    private const int MaxSitemapDepth = 5;

    /// <summary>
    /// The maximum number of sitemap documents (indexes and leaf sitemaps combined) the crawler downloads
    /// while discovering page URLs for a single site.
    /// </summary>
    private const int MaxSitemapDocuments = 50;

    private readonly DocumentationSite _site;
    private readonly DocumentationSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SitemapDocumentationSource"/> class.
    /// </summary>
    /// <param name="site">The site configuration.</param>
    /// <param name="options">The global documentation search options.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public SitemapDocumentationSource(
        DocumentationSite site,
        DocumentationSearchOptions options,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger logger)
        : base(site.Name, options.CacheDuration, timeProvider, options.FirstSearchWaitBudget)
    {
        _site = site;
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override int MaxResults => _site.MaxResults ?? _options.MaxResultsPerSite;

    /// <inheritdoc />
    protected override async Task<DocumentationCorpus> BuildCorpusAsync(CancellationToken cancellationToken)
    {
        var entries = await CrawlAsync(cancellationToken);

        return new DocumentationCorpus(entries);
    }

    private async Task<IReadOnlyList<DocumentationCorpus.Entry>> CrawlAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(DocumentationToolConstants.HttpClientName);
        var maxPages = Math.Max(1, _site.MaxPages ?? _options.MaxPagesPerSite);
        var urls = await GetSitemapUrlsAsync(client, maxPages, cancellationToken);

        if (urls.Count == 0)
        {
            _logger.LogWarning(
                "Documentation source '{SourceName}' discovered no page URLs from its sitemap. Verify the sitemap URL and that the site is reachable.",
                _site.Name);

            return [];
        }

        var pages = new List<DocumentationCorpus.Entry>(urls.Count);
        using var throttle = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRequests));

        var tasks = urls.Select(async url =>
        {
            await throttle.WaitAsync(cancellationToken);

            try
            {
                return await FetchPageAsync(client, url, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        });

        foreach (var page in await Task.WhenAll(tasks))
        {
            if (page is not null)
            {
                pages.Add(page);
            }
        }

        return pages;
    }

    /// <summary>
    /// Discovers the page URLs for the configured site by walking its sitemap graph. Sitemap index files
    /// are followed into their child sitemaps, gzip-compressed and plain-text sitemaps are supported, and
    /// discovery falls back to <c>robots.txt</c> and the well-known sitemap locations when no explicit
    /// sitemap URL is configured.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetSitemapUrlsAsync(HttpClient client, int maxPages, CancellationToken cancellationToken)
    {
        var seeds = await ResolveSitemapSeedsAsync(client, cancellationToken);

        var pageUrls = new List<string>();
        var seenPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedSitemaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Url, int Depth)>();

        foreach (var seed in seeds)
        {
            if (visitedSitemaps.Add(seed))
            {
                queue.Enqueue((seed, 0));
            }
        }

        var processedSitemaps = 0;

        while (queue.Count > 0 && pageUrls.Count < maxPages && processedSitemaps < MaxSitemapDocuments)
        {
            var (sitemapUrl, depth) = queue.Dequeue();
            processedSitemaps++;

            var content = await DownloadSitemapContentAsync(client, sitemapUrl, cancellationToken);

            if (content is null)
            {
                continue;
            }

            var (childSitemaps, pages) = ParseSitemap(content);

            foreach (var page in pages)
            {
                if (pageUrls.Count >= maxPages)
                {
                    break;
                }

                if (seenPages.Add(page))
                {
                    pageUrls.Add(page);
                }
            }

            if (depth < MaxSitemapDepth)
            {
                foreach (var child in childSitemaps)
                {
                    if (visitedSitemaps.Add(child))
                    {
                        queue.Enqueue((child, depth + 1));
                    }
                }
            }
        }

        return pageUrls;
    }

    /// <summary>
    /// Determines the sitemap URLs to start crawling from. An explicitly configured sitemap URL wins;
    /// otherwise the crawler consults <c>robots.txt</c> and the conventional sitemap locations under the
    /// base URL.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveSitemapSeedsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var seeds = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string url)
        {
            if (!string.IsNullOrWhiteSpace(url) && seen.Add(url.Trim()))
            {
                seeds.Add(url.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(_site.SitemapUrl))
        {
            Add(_site.SitemapUrl);

            return seeds;
        }

        if (!string.IsNullOrWhiteSpace(_site.BaseUrl))
        {
            foreach (var advertised in await GetRobotsSitemapsAsync(client, cancellationToken))
            {
                Add(advertised);
            }

            var baseUrl = _site.BaseUrl.TrimEnd('/');

            // The two conventional locations. Yoast/Rank Math sites typically redirect /sitemap.xml to
            // their index; HttpClient follows those redirects, and unreachable candidates fail gracefully.
            Add($"{baseUrl}/sitemap.xml");
            Add($"{baseUrl}/sitemap_index.xml");
        }

        return seeds;
    }

    /// <summary>
    /// Reads any <c>Sitemap:</c> directives advertised in the site's <c>robots.txt</c>.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetRobotsSitemapsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        const string directive = "Sitemap:";
        var baseUrl = _site.BaseUrl.TrimEnd('/');
        var robotsUrl = $"{baseUrl}/robots.txt";

        try
        {
            var content = await client.GetStringAsync(robotsUrl, cancellationToken);
            var results = new List<string>();

            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith(directive, StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed[directive.Length..].Trim();

                    if (!string.IsNullOrEmpty(value))
                    {
                        results.Add(value);
                    }
                }
            }

            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Failed to read robots.txt '{RobotsUrl}' for documentation source '{SourceName}'.", robotsUrl, _site.Name);
            }

            return [];
        }
    }

    /// <summary>
    /// Downloads a sitemap document and returns its decoded text, transparently decompressing gzip
    /// payloads (both <c>.xml.gz</c> files and gzip-encoded responses).
    /// </summary>
    private async Task<string> DownloadSitemapContentAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await client.GetByteArrayAsync(url, cancellationToken);

            if (bytes.Length == 0)
            {
                return null;
            }

            // Gzip files begin with the magic bytes 0x1F 0x8B. This covers .xml.gz sitemaps served with a
            // binary content type, which HttpClient's automatic decompression does not unwrap.
            if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            {
                bytes = Decompress(bytes);
            }

            return DecodeText(bytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read sitemap '{SitemapUrl}' for documentation source '{SourceName}'.", url, _site.Name);

            return null;
        }
    }

    /// <summary>
    /// Parses a sitemap document, separating child sitemap URLs (from a sitemap index) from page URLs.
    /// Supports the standard <c>&lt;urlset&gt;</c> and <c>&lt;sitemapindex&gt;</c> XML formats (including
    /// the News/Image/Video extensions, whose page URL is the <c>&lt;url&gt;&lt;loc&gt;</c>), RSS 2.0 and
    /// Atom 1.0 feeds (which Google also accepts as sitemaps), and plain-text sitemaps. Page URLs are
    /// classified by their parent element so that asset locations such as <c>&lt;image:loc&gt;</c> and
    /// <c>&lt;video:loc&gt;</c> are ignored.
    /// </summary>
    private static (IReadOnlyList<string> ChildSitemaps, IReadOnlyList<string> Pages) ParseSitemap(string content)
    {
        var childSitemaps = new List<string>();
        var pages = new List<string>();

        XDocument document = null;

        try
        {
            document = XDocument.Parse(content);
        }
        catch
        {
            // Not XML. Fall through to the plain-text sitemap handling below.
        }

        if (document is not null)
        {
            foreach (var loc in document.Descendants().Where(element => string.Equals(element.Name.LocalName, "loc", StringComparison.OrdinalIgnoreCase)))
            {
                var value = loc.Value?.Trim();

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var parentName = loc.Parent?.Name.LocalName;

                if (string.Equals(parentName, "sitemap", StringComparison.OrdinalIgnoreCase))
                {
                    childSitemaps.Add(value);
                }
                else if (string.Equals(parentName, "url", StringComparison.OrdinalIgnoreCase))
                {
                    pages.Add(value);
                }

                // Any other parent (image, video, news, ...) is asset metadata and is intentionally skipped.
            }

            // No sitemaps.org <loc> elements were found. The document may be an RSS 2.0 or Atom 1.0 feed,
            // which Google also accepts as a sitemap format.
            if (childSitemaps.Count == 0 && pages.Count == 0)
            {
                ExtractFeedUrls(document, pages);
            }

            return (childSitemaps, pages);
        }

        // Plain-text sitemap: one absolute URL per line.
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                pages.Add(trimmed);
            }
        }

        return (childSitemaps, pages);
    }

    /// <summary>
    /// Extracts item/entry URLs from an RSS 2.0 or Atom 1.0 feed. RSS items carry the URL as the text of a
    /// <c>&lt;link&gt;</c> element; Atom entries carry it in the <c>href</c> attribute of an
    /// <c>&lt;link&gt;</c> element, preferring the <c>alternate</c> relation. Feed-level links (the channel
    /// or feed homepage) are ignored because only <c>&lt;item&gt;</c> and <c>&lt;entry&gt;</c> links are read.
    /// </summary>
    private static void ExtractFeedUrls(XDocument document, List<string> pages)
    {
        foreach (var element in document.Descendants())
        {
            var name = element.Name.LocalName;

            if (string.Equals(name, "item", StringComparison.OrdinalIgnoreCase))
            {
                var link = element.Elements()
                    .FirstOrDefault(child => string.Equals(child.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase));

                // RSS uses the element text; some feeds instead carry an Atom-style href.
                var url = link?.Value?.Trim();

                if (string.IsNullOrEmpty(url))
                {
                    url = link?.Attribute("href")?.Value?.Trim();
                }

                if (!string.IsNullOrEmpty(url))
                {
                    pages.Add(url);
                }
            }
            else if (string.Equals(name, "entry", StringComparison.OrdinalIgnoreCase))
            {
                var links = element.Elements()
                    .Where(child => string.Equals(child.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Prefer the alternate (or unspecified) relation; never the self/edit/enclosure links.
                var preferred = links.FirstOrDefault(child =>
                {
                    var rel = child.Attribute("rel")?.Value;

                    return string.IsNullOrEmpty(rel) || string.Equals(rel, "alternate", StringComparison.OrdinalIgnoreCase);
                }) ?? links.FirstOrDefault();

                var url = preferred?.Attribute("href")?.Value?.Trim();

                if (!string.IsNullOrEmpty(url))
                {
                    pages.Add(url);
                }
            }
        }
    }

    private async Task<DocumentationCorpus.Entry> FetchPageAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            var html = await client.GetStringAsync(url, cancellationToken);
            var title = ExtractTitle(html);
            var text = ExtractText(html);

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return new DocumentationCorpus.Entry(url, title, text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Failed to fetch documentation page '{Url}' for source '{SourceName}'.", url, _site.Name);
            }

            return null;
        }
    }

    private static byte[] Decompress(byte[] input)
    {
        using var source = new MemoryStream(input);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var destination = new MemoryStream();

        gzip.CopyTo(destination);

        return destination.ToArray();
    }

    private static string DecodeText(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);

        // Strip a leading UTF-8 byte-order mark (U+FEFF) so XDocument.Parse does not choke on it.
        return text.TrimStart('﻿');
    }

    private static string ExtractTitle(string html)
    {
        var match = TitleRegex().Match(html);

        if (!match.Success)
        {
            return null;
        }

        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
    }

    private static string ExtractText(string html)
    {
        var withoutScripts = ScriptRegex().Replace(html, " ");
        var withoutStyles = StyleRegex().Replace(withoutScripts, " ");
        var withoutTags = TagRegex().Replace(withoutStyles, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);

        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex(@"<script\b[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<style\b[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleRegex();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
