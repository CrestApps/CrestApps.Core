namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// Global defaults for web crawlers. A crawler's own strategy settings override these per site; unset
/// values fall back here.
/// </summary>
public sealed class WebCrawlerOptions
{
    /// <summary>
    /// Gets or sets the default maximum number of pages scraped per crawler.
    /// </summary>
    public int DefaultMaxPages { get; set; } = 500;

    /// <summary>
    /// Gets or sets the default maximum number of concurrent page requests per crawler.
    /// </summary>
    public int DefaultMaxConcurrentRequests { get; set; } = 4;

    /// <summary>
    /// Gets or sets the default per-request timeout, in seconds, when fetching a page.
    /// </summary>
    public int DefaultRequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the default <c>User-Agent</c> header presented while crawling.
    /// </summary>
    public string DefaultUserAgent { get; set; } = "CrestApps-WebCrawler/1.0 (+https://crestapps.com)";

    /// <summary>
    /// Gets or sets the default re-index interval, in minutes, applied when a crawler does not specify its
    /// own.
    /// </summary>
    public int DefaultReindexIntervalMinutes { get; set; } = (int)TimeSpan.FromHours(24).TotalMinutes;

    /// <summary>
    /// Gets or sets how often, in minutes, the background service wakes to check whether any crawler is due
    /// for a re-index.
    /// </summary>
    public int ReindexCheckIntervalMinutes { get; set; } = 15;
}
