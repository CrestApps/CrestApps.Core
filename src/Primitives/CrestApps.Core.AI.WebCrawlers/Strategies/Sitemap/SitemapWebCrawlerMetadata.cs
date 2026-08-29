namespace CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;

/// <summary>
/// Settings for the sitemap crawl strategy, stored in a <see cref="CrestApps.Core.AI.Models.WebCrawler"/>'s
/// properties: which site to scrape and how much of it.
/// </summary>
public sealed class SitemapWebCrawlerMetadata
{
    /// <summary>
    /// Gets or sets the base URL of the site to scrape. Used to resolve the sitemap through
    /// <c>robots.txt</c> and the conventional locations when <see cref="SitemapUrl"/> is not supplied.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets an explicit sitemap or sitemap-index URL. When set, discovery starts from this URL.
    /// </summary>
    public string SitemapUrl { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pages to scrape. When not set, the global default is used.
    /// </summary>
    public int? MaxPages { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent page requests. When not set, the global default is used.
    /// </summary>
    public int? MaxConcurrentRequests { get; set; }

    /// <summary>
    /// Gets or sets the per-request fetch timeout, in seconds. When not set, the global default is used.
    /// </summary>
    public int? RequestTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the regular-expression patterns a page URL must match to be scraped. When empty, every
    /// discovered page is eligible.
    /// </summary>
    public List<string> IncludeUrlPatterns { get; set; }

    /// <summary>
    /// Gets or sets the regular-expression patterns that exclude a page URL from scraping. Applied after
    /// <see cref="IncludeUrlPatterns"/>.
    /// </summary>
    public List<string> ExcludeUrlPatterns { get; set; }

    /// <summary>
    /// Gets or sets the <c>User-Agent</c> header presented while crawling. When not set, the global default
    /// is used.
    /// </summary>
    public string UserAgent { get; set; }
}
