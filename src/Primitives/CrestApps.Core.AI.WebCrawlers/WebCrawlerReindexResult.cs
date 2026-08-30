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
}

/// <summary>
/// Summarizes the outcome of a web-crawler re-index plan: how many pages were newly discovered, changed,
/// removed, or left unchanged since the previous run, along with an overall <see cref="Status"/> so callers
/// can surface blocked or unreachable sites to the user.
/// </summary>
/// <param name="NewCount">The number of newly discovered pages queued for indexing.</param>
/// <param name="ChangedCount">The number of changed pages queued for re-indexing.</param>
/// <param name="RemovedCount">The number of removed pages queued for deletion.</param>
/// <param name="UnchangedCount">The number of discovered pages that did not change.</param>
public sealed record WebCrawlerReindexResult(
    int NewCount,
    int ChangedCount,
    int RemovedCount,
    int UnchangedCount)
{
    /// <summary>
    /// An empty result for crawlers that produced no work.
    /// </summary>
    public static readonly WebCrawlerReindexResult Empty = new(0, 0, 0, 0) { Status = WebCrawlerReindexStatus.Skipped };

    /// <summary>
    /// The overall outcome of the plan.
    /// </summary>
    public WebCrawlerReindexStatus Status { get; init; } = WebCrawlerReindexStatus.Completed;

    /// <summary>
    /// The total number of pages discovered by the crawler on this run.
    /// </summary>
    public int DiscoveredCount { get; init; }

    /// <summary>
    /// A human-readable message describing a non-completed outcome (blocked, unreachable, failed), or
    /// <see langword="null"/> when the plan completed normally.
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// Creates a result indicating discovery returned no pages.
    /// </summary>
    /// <param name="message">The human-readable explanation.</param>
    public static WebCrawlerReindexResult NoPagesDiscovered(string message)
        => new(0, 0, 0, 0) { Status = WebCrawlerReindexStatus.NoPagesDiscovered, Message = message };

    /// <summary>
    /// Creates a result indicating discovery failed.
    /// </summary>
    /// <param name="message">The human-readable explanation.</param>
    public static WebCrawlerReindexResult Failed(string message)
        => new(0, 0, 0, 0) { Status = WebCrawlerReindexStatus.DiscoveryFailed, Message = message };
}
