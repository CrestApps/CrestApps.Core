namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// The outcome status of a web-crawler re-index plan.
/// </summary>
public enum WebCrawlerReindexStatus
{
    /// <summary>
    /// The crawler was discovered and its changes (if any) were planned and enqueued.
    /// </summary>
    Completed,

    /// <summary>
    /// The crawler is disabled, unconfigured, or its strategy is not registered, so no work was planned.
    /// </summary>
    Skipped,

    /// <summary>
    /// Discovery ran but returned no pages. The existing crawl state was left untouched (so a transient
    /// block does not wipe the knowledge base). The site is likely blocking the crawler, or the base/sitemap
    /// URL is wrong, unreachable, or empty.
    /// </summary>
    NoPagesDiscovered,

    /// <summary>
    /// Discovery threw. The site could not be crawled at all (network failure, or the crawler was blocked).
    /// </summary>
    DiscoveryFailed,

    /// <summary>
    /// Discovery ran but did not cover the whole site - a page limit was reached, a sitemap was unreachable,
    /// or the sitemap graph was larger than the crawler follows. New and changed pages were still indexed,
    /// and <b>nothing was removed</b>: a page missing from a partial crawl is missing because the crawl
    /// stopped short, not because the site removed it.
    /// </summary>
    PartiallyDiscovered,
}
