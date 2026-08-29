namespace CrestApps.Core.AI.WebCrawlers.Crawling;

/// <summary>
/// Describes a single sitemap discovery run for the <see cref="ISitemapCrawler"/>. Either
/// <see cref="SitemapUrl"/> or <see cref="BaseUrl"/> must be supplied; an explicit sitemap URL wins,
/// otherwise the crawler resolves the sitemap from <c>robots.txt</c> and the conventional locations
/// under <see cref="BaseUrl"/>.
/// </summary>
public sealed class SitemapCrawlRequest
{
    /// <summary>
    /// Gets or sets the base URL of the site to crawl (for example <c>https://docs.example.com</c>). Used
    /// to resolve the sitemap when <see cref="SitemapUrl"/> is not supplied.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets an explicit sitemap URL. When set, discovery starts from this URL and does not consult
    /// <c>robots.txt</c> or the conventional locations.
    /// </summary>
    public string SitemapUrl { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of page URLs to discover. Discovery stops once this many pages have
    /// been found, bounding the work for very large sites.
    /// </summary>
    public int MaxPages { get; set; } = int.MaxValue;
}
