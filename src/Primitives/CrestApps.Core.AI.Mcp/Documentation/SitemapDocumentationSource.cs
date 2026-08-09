using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// A built-in <see cref="IDocumentationSource"/> that indexes a public documentation site by reading
/// its <c>sitemap.xml</c>, fetching each page, and ranking pages against a query using lightweight
/// keyword scoring. The crawled corpus is cached in memory and refreshed based on
/// <see cref="DocumentationSearchOptions.CacheDuration"/>.
/// </summary>
public sealed partial class SitemapDocumentationSource : CachingDocumentationSource
{
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
        : base(site.Name, options.CacheDuration, timeProvider)
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
        var client = _httpClientFactory.CreateClient(McpConstants.DocumentationHttpClientName);
        var urls = await GetSitemapUrlsAsync(client, cancellationToken);

        if (urls.Count == 0)
        {
            return [];
        }

        var maxPages = _site.MaxPages ?? _options.MaxPagesPerSite;

        if (urls.Count > maxPages)
        {
            urls = urls.Take(maxPages).ToList();
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

    private async Task<IReadOnlyList<string>> GetSitemapUrlsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var sitemapUrl = ResolveSitemapUrl();

        try
        {
            var xml = await client.GetStringAsync(sitemapUrl, cancellationToken);
            var document = XDocument.Parse(xml);

            var urls = document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "loc", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Value?.Trim())
                .Where(value => !string.IsNullOrEmpty(value) && !value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return urls;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read sitemap '{SitemapUrl}' for documentation source '{SourceName}'.", sitemapUrl, _site.Name);

            return [];
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
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Failed to fetch documentation page '{Url}' for source '{SourceName}'.", url, _site.Name);
            }

            return null;
        }
    }

    private string ResolveSitemapUrl()
    {
        if (!string.IsNullOrWhiteSpace(_site.SitemapUrl))
        {
            return _site.SitemapUrl;
        }

        return $"{_site.BaseUrl.TrimEnd('/')}/sitemap.xml";
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
