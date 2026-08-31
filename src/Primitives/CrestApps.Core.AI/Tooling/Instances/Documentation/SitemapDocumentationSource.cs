using System.Text.RegularExpressions;
using CrestApps.Core.AI.Crawling;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// A built-in <see cref="IDocumentationSource"/> that indexes a public documentation site by reading
/// its sitemap, fetching each page, and ranking pages against a query using lightweight keyword scoring.
/// Sitemap discovery is delegated to the shared <see cref="ISitemapCrawler"/>, so the crawler understands
/// the full sitemaps.org protocol as it appears on the market (flat <c>&lt;urlset&gt;</c>, nested
/// <c>&lt;sitemapindex&gt;</c>, gzip-compressed and plain-text sitemaps, RSS/Atom feeds, and
/// <c>robots.txt</c> advertisements). The crawled corpus is cached in memory and refreshed based on
/// <see cref="DocumentationSearchOptions.CacheDuration"/>.
/// </summary>
public sealed partial class SitemapDocumentationSource : CachingDocumentationSource
{
    private readonly DocumentationSite _site;
    private readonly DocumentationSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISitemapCrawler _sitemapCrawler;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SitemapDocumentationSource"/> class.
    /// </summary>
    /// <param name="site">The site configuration.</param>
    /// <param name="options">The global documentation search options.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="sitemapCrawler">The shared sitemap crawler used to discover page URLs.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public SitemapDocumentationSource(
        DocumentationSite site,
        DocumentationSearchOptions options,
        IHttpClientFactory httpClientFactory,
        ISitemapCrawler sitemapCrawler,
        TimeProvider timeProvider,
        ILogger logger)
        : base(site.Name, options.CacheDuration, timeProvider, options.FirstSearchWaitBudget)
    {
        _site = site;
        _options = options;
        _httpClientFactory = httpClientFactory;
        _sitemapCrawler = sitemapCrawler;
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

        var discovered = await _sitemapCrawler.DiscoverAsync(
            client,
            new SitemapCrawlRequest
            {
                BaseUrl = _site.BaseUrl,
                SitemapUrl = _site.SitemapUrl,
                MaxPages = maxPages,
            },
            cancellationToken);

        if (discovered.Count == 0)
        {
            _logger.LogWarning(
                "Documentation source '{SourceName}' discovered no page URLs from its sitemap. Verify the sitemap URL and that the site is reachable.",
                _site.Name);

            return [];
        }

        var pages = new List<DocumentationCorpus.Entry>(discovered.Count);
        using var throttle = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRequests));

        var tasks = discovered.Select(async entry =>
        {
            await throttle.WaitAsync(cancellationToken);

            try
            {
                return await FetchPageAsync(client, entry.Url, cancellationToken);
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
