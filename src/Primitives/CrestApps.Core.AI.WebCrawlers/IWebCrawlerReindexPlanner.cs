using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// Computes and enqueues the incremental re-index work for one <see cref="WebCrawler"/> by diffing a fresh
/// crawl against the recorded per-page crawl state. This is the reusable unit of work the background
/// service (or any host that wants its own scheduling) drives.
/// </summary>
public interface IWebCrawlerReindexPlanner
{
    /// <summary>
    /// Re-crawls the site, diffs it against the stored crawl state, and enqueues the pages that were added,
    /// changed, or removed into the crawler's target data source. Unchanged pages are skipped.
    /// </summary>
    /// <param name="crawler">The crawler to re-index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A summary of the planned work.</returns>
    Task<WebCrawlerReindexResult> PlanAndEnqueueAsync(WebCrawler crawler, CancellationToken cancellationToken = default);
}
