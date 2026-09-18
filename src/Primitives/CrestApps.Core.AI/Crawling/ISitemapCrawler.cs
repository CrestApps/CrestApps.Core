namespace CrestApps.Core.AI.Crawling;

/// <summary>
/// Discovers page URLs for a site by walking its sitemap graph. The crawler understands the full
/// sitemaps.org protocol as it appears on the market: a flat <c>&lt;urlset&gt;</c>, a
/// <c>&lt;sitemapindex&gt;</c> that nests child sitemaps (for example those emitted by Yoast, Rank Math,
/// or Google), gzip-compressed sitemaps (<c>.xml.gz</c>), plain-text sitemaps, RSS 2.0 and Atom 1.0
/// feeds (which Google also accepts as sitemaps), and sitemaps advertised through <c>robots.txt</c>.
/// </summary>
public interface ISitemapCrawler
{
    /// <summary>
    /// Discovers the page entries and says whether the walk covered the whole sitemap graph.
    /// </summary>
    /// <param name="client">The HTTP client used to download sitemap and <c>robots.txt</c> documents.</param>
    /// <param name="request">The discovery request describing the site and limits.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entries, and whether they are all of them.</returns>
    /// <remarks>
    /// A walk stops short when the page cap is reached, when the sitemap graph is larger than the crawler
    /// will follow, or when a child sitemap could not be downloaded. A caller that deletes what it did not
    /// see has to be able to tell those apart from a site that genuinely shrank.
    /// </remarks>
    Task<SitemapDiscoveryResult> DiscoverDetailedAsync(
        HttpClient client,
        SitemapCrawlRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers the page entries for the supplied request by walking its sitemap graph.
    /// </summary>
    /// <param name="client">The HTTP client used to download sitemap and <c>robots.txt</c> documents.</param>
    /// <param name="request">The discovery request describing the site and limits.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The discovered page entries, de-duplicated by URL and bounded by the request limit.</returns>
    Task<IReadOnlyList<SitemapEntry>> DiscoverAsync(
        HttpClient client,
        SitemapCrawlRequest request,
        CancellationToken cancellationToken = default);
}
