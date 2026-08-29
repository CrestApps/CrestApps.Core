using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Crawling;

/// <summary>
/// The default <see cref="ISitemapCrawler"/>. It resolves the sitemap seeds for a site, walks any
/// sitemap index into its child sitemaps, and returns the discovered page entries together with the
/// change metadata each sitemap advertises. Discovery is bounded so a misconfigured or hostile site
/// cannot loop forever or download an unbounded number of documents.
/// </summary>
public sealed class SitemapCrawler : ISitemapCrawler
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

    private readonly ILogger<SitemapCrawler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SitemapCrawler"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public SitemapCrawler(ILogger<SitemapCrawler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SitemapEntry>> DiscoverAsync(
        HttpClient client,
        SitemapCrawlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);

        var maxPages = Math.Max(1, request.MaxPages);
        var seeds = await ResolveSitemapSeedsAsync(client, request, cancellationToken);

        var entries = new List<SitemapEntry>();
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

        while (queue.Count > 0 && entries.Count < maxPages && processedSitemaps < MaxSitemapDocuments)
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
                if (entries.Count >= maxPages)
                {
                    break;
                }

                if (seenPages.Add(page.Url))
                {
                    entries.Add(page);
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

        return entries;
    }

    /// <summary>
    /// Determines the sitemap URLs to start crawling from. An explicitly configured sitemap URL wins;
    /// otherwise the crawler consults <c>robots.txt</c> and the conventional sitemap locations under the
    /// base URL.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveSitemapSeedsAsync(HttpClient client, SitemapCrawlRequest request, CancellationToken cancellationToken)
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

        if (!string.IsNullOrWhiteSpace(request.SitemapUrl))
        {
            Add(request.SitemapUrl);

            return seeds;
        }

        if (!string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            foreach (var advertised in await GetRobotsSitemapsAsync(client, request.BaseUrl, cancellationToken))
            {
                Add(advertised);
            }

            var baseUrl = request.BaseUrl.TrimEnd('/');

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
    private async Task<IReadOnlyList<string>> GetRobotsSitemapsAsync(HttpClient client, string baseUrl, CancellationToken cancellationToken)
    {
        const string directive = "Sitemap:";
        var robotsUrl = $"{baseUrl.TrimEnd('/')}/robots.txt";

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
                _logger.LogDebug(ex, "Failed to read robots.txt '{RobotsUrl}' during sitemap discovery.", robotsUrl);
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
            _logger.LogWarning(ex, "Failed to read sitemap '{SitemapUrl}' during sitemap discovery.", url);

            return null;
        }
    }

    /// <summary>
    /// Parses a sitemap document, separating child sitemap URLs (from a sitemap index) from page entries.
    /// Supports the standard <c>&lt;urlset&gt;</c> and <c>&lt;sitemapindex&gt;</c> XML formats (including
    /// the News/Image/Video extensions, whose page URL is the <c>&lt;url&gt;&lt;loc&gt;</c>), RSS 2.0 and
    /// Atom 1.0 feeds (which Google also accepts as sitemaps), and plain-text sitemaps. Page entries are
    /// classified by their parent element so that asset locations such as <c>&lt;image:loc&gt;</c> and
    /// <c>&lt;video:loc&gt;</c> are ignored.
    /// </summary>
    private static (IReadOnlyList<string> ChildSitemaps, IReadOnlyList<SitemapEntry> Pages) ParseSitemap(string content)
    {
        var childSitemaps = new List<string>();
        var pages = new List<SitemapEntry>();

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
            foreach (var element in document.Descendants())
            {
                var name = element.Name.LocalName;

                if (string.Equals(name, "sitemap", StringComparison.OrdinalIgnoreCase))
                {
                    var loc = GetChildValue(element, "loc");

                    if (!string.IsNullOrEmpty(loc))
                    {
                        childSitemaps.Add(loc);
                    }
                }
                else if (string.Equals(name, "url", StringComparison.OrdinalIgnoreCase))
                {
                    var loc = GetChildValue(element, "loc");

                    if (!string.IsNullOrEmpty(loc))
                    {
                        pages.Add(new SitemapEntry(
                            loc,
                            ParseLastModified(GetChildValue(element, "lastmod")),
                            NormalizeChangeFrequency(GetChildValue(element, "changefreq")),
                            ParsePriority(GetChildValue(element, "priority"))));
                    }
                }
            }

            // No sitemaps.org <url>/<sitemap> elements were found. The document may be an RSS 2.0 or Atom
            // 1.0 feed, which Google also accepts as a sitemap format.
            if (childSitemaps.Count == 0 && pages.Count == 0)
            {
                ExtractFeedEntries(document, pages);
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
                pages.Add(new SitemapEntry(trimmed));
            }
        }

        return (childSitemaps, pages);
    }

    /// <summary>
    /// Extracts item/entry URLs from an RSS 2.0 or Atom 1.0 feed. RSS items carry the URL as the text of a
    /// <c>&lt;link&gt;</c> element and a timestamp in <c>&lt;pubDate&gt;</c>; Atom entries carry the URL in
    /// the <c>href</c> attribute of an <c>&lt;link&gt;</c> element (preferring the <c>alternate</c>
    /// relation) and a timestamp in <c>&lt;updated&gt;</c>. Feed-level links (the channel or feed homepage)
    /// are ignored because only <c>&lt;item&gt;</c> and <c>&lt;entry&gt;</c> links are read.
    /// </summary>
    private static void ExtractFeedEntries(XDocument document, List<SitemapEntry> pages)
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
                    pages.Add(new SitemapEntry(url, ParseLastModified(GetChildValue(element, "pubDate"))));
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
                    pages.Add(new SitemapEntry(url, ParseLastModified(GetChildValue(element, "updated"))));
                }
            }
        }
    }

    private static string GetChildValue(XElement parent, string localName)
    {
        var child = parent.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

        return child?.Value?.Trim();
    }

    private static DateTimeOffset? ParseLastModified(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string NormalizeChangeFrequency(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static double? ParsePriority(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
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
}
