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
public sealed partial class SitemapDocumentationSource : IDocumentationSource
{
    private readonly DocumentationSite _site;
    private readonly DocumentationSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private IReadOnlyList<DocumentationPage> _pages = [];
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

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
    {
        _site = site;
        _options = options;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => _site.Name;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentationSearchResult>> SearchAsync(DocumentationSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        var pages = await GetPagesAsync(cancellationToken);

        if (pages.Count == 0)
        {
            return [];
        }

        var terms = Tokenize(request.Query);

        if (terms.Length == 0)
        {
            return [];
        }

        var maxResults = _site.MaxResults ?? _options.MaxResultsPerSite;

        var scored = new List<DocumentationSearchResult>();

        foreach (var page in pages)
        {
            var score = Score(page, terms);

            if (score <= 0)
            {
                continue;
            }

            scored.Add(new DocumentationSearchResult
            {
                SourceName = _site.Name,
                Title = string.IsNullOrWhiteSpace(page.Title) ? page.Url : page.Title,
                Url = page.Url,
                Snippet = BuildSnippet(page.Text, terms),
                Score = score,
            });
        }

        return scored
            .OrderByDescending(result => result.Score)
            .Take(Math.Min(request.MaxResults, maxResults))
            .ToList();
    }

    private async Task<IReadOnlyList<DocumentationPage>> GetPagesAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (_pages.Count > 0 && now - _loadedAt < _options.CacheDuration)
        {
            return _pages;
        }

        await _loadLock.WaitAsync(cancellationToken);

        try
        {
            now = _timeProvider.GetUtcNow();

            if (_pages.Count > 0 && now - _loadedAt < _options.CacheDuration)
            {
                return _pages;
            }

            _pages = await CrawlAsync(cancellationToken);
            _loadedAt = _timeProvider.GetUtcNow();

            return _pages;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<IReadOnlyList<DocumentationPage>> CrawlAsync(CancellationToken cancellationToken)
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

        var pages = new List<DocumentationPage>(urls.Count);
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

    private async Task<DocumentationPage> FetchPageAsync(HttpClient client, string url, CancellationToken cancellationToken)
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

            return new DocumentationPage(url, title, text);
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

    private static double Score(DocumentationPage page, string[] terms)
    {
        double score = 0;
        var matchedTerms = 0;

        foreach (var term in terms)
        {
            var bodyCount = CountOccurrences(page.Text, term);
            var titleCount = CountOccurrences(page.Title, term);

            if (bodyCount == 0 && titleCount == 0)
            {
                continue;
            }

            matchedTerms++;
            score += bodyCount + (titleCount * 5);
        }

        if (matchedTerms == 0)
        {
            return 0;
        }

        return score * matchedTerms;
    }

    private static int CountOccurrences(string text, string term)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += term.Length;
        }

        return count;
    }

    private static string BuildSnippet(string text, string[] terms)
    {
        const int windowSize = 240;

        var matchIndex = -1;

        foreach (var term in terms)
        {
            var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);

            if (index >= 0 && (matchIndex < 0 || index < matchIndex))
            {
                matchIndex = index;
            }
        }

        if (matchIndex < 0)
        {
            return text.Length <= windowSize ? text : text[..windowSize] + "…";
        }

        var start = Math.Max(0, matchIndex - (windowSize / 2));
        var length = Math.Min(windowSize, text.Length - start);
        var snippet = text.Substring(start, length).Trim();

        if (start > 0)
        {
            snippet = "…" + snippet;
        }

        if (start + length < text.Length)
        {
            snippet += "…";
        }

        return snippet;
    }

    private static string[] Tokenize(string query)
    {
        return NonWordRegex()
            .Split(query.ToLowerInvariant())
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWordRegex();

    private sealed record DocumentationPage(string Url, string Title, string Text);
}
