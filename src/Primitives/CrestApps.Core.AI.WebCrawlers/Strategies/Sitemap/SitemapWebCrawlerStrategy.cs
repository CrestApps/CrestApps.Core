using System.ComponentModel.DataAnnotations;
using System.Text;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Crawling;
using CrestApps.Core.DataIngestion;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;

/// <summary>
/// The sitemap crawl strategy. It discovers pages by walking a site's sitemap(s) with the shared
/// <see cref="ISitemapCrawler"/> and fetches each page as cleaned plain text through the reusable
/// <see cref="HtmlIngestionDocumentReader"/>.
/// </summary>
public sealed class SitemapWebCrawlerStrategy : IWebCrawlerStrategy
{
    private readonly ISitemapCrawler _sitemapCrawler;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebCrawlerOptions _options;
    private readonly ILogger<SitemapWebCrawlerStrategy> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SitemapWebCrawlerStrategy"/> class.
    /// </summary>
    /// <param name="sitemapCrawler">The sitemap crawler.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="options">The web-crawler options.</param>
    /// <param name="logger">The logger.</param>
    public SitemapWebCrawlerStrategy(
        ISitemapCrawler sitemapCrawler,
        IHttpClientFactory httpClientFactory,
        IOptions<WebCrawlerOptions> options,
        ILogger<SitemapWebCrawlerStrategy> logger)
    {
        _sitemapCrawler = sitemapCrawler;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => WebCrawlerConstants.Strategies.Sitemap;

    /// <inheritdoc />
    public ValueTask ValidateAsync(WebCrawler crawler, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crawler);
        ArgumentNullException.ThrowIfNull(result);

        crawler.TryGet<SitemapWebCrawlerMetadata>(out var metadata);
        var hasBaseUrl = TryCreateAbsoluteUrl(metadata?.BaseUrl, out _);
        var hasSitemapUrl = TryCreateAbsoluteUrl(metadata?.SitemapUrl, out _);

        if (!hasBaseUrl && !hasSitemapUrl)
        {
            result.Fail(new ValidationResult(
                "A valid website base URL or sitemap URL is required.",
                [nameof(SitemapWebCrawlerMetadata.BaseUrl), nameof(SitemapWebCrawlerMetadata.SitemapUrl)]));
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrawledPageRef>> DiscoverAsync(WebCrawler crawler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crawler);

        if (!SitemapWebCrawlerHelper.TryGetMetadata(crawler, out var metadata))
        {
            return [];
        }

        var client = _httpClientFactory.CreateClient(WebCrawlerConstants.HttpClientName);
        var filter = SitemapWebCrawlerHelper.CreateUrlFilter(metadata);

        var discovered = await _sitemapCrawler.DiscoverAsync(
            client,
            SitemapWebCrawlerHelper.CreateCrawlRequest(metadata, _options),
            cancellationToken);

        return discovered
            .Where(entry => filter(entry.Url))
            .Select(entry => new CrawledPageRef(entry.Url, entry.LastModifiedUtc, entry.ChangeFrequency))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<CrawledPage> FetchAsync(WebCrawler crawler, string url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crawler);

        if (string.IsNullOrWhiteSpace(url) || !SitemapWebCrawlerHelper.TryGetMetadata(crawler, out var metadata))
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient(WebCrawlerConstants.HttpClientName);
        var userAgent = SitemapWebCrawlerHelper.ResolveUserAgent(metadata, _options);
        var requestTimeout = SitemapWebCrawlerHelper.ResolveRequestTimeout(metadata, _options);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(requestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(timeoutSource.Token);
            var title = HtmlIngestionDocumentReader.ExtractTitle(html);
            var document = HtmlIngestionDocumentReader.Read(html, url);
            var content = FlattenDocument(document);

            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            return new CrawledPage(title, content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Failed to fetch page '{Url}' for web crawler '{CrawlerId}'.", url, crawler.ItemId);
            }

            return null;
        }
    }

    private static string FlattenDocument(Microsoft.Extensions.DataIngestion.IngestionDocument document)
    {
        var builder = new StringBuilder();

        foreach (var element in document.EnumerateContent())
        {
            if (string.IsNullOrWhiteSpace(element.Text))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(element.Text);
        }

        return builder.ToString();
    }

    private static bool TryCreateAbsoluteUrl(string value, out Uri uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
