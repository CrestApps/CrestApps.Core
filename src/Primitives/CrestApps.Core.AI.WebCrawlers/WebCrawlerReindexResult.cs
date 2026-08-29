namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// Summarizes the outcome of a web-crawler re-index plan: how many pages were newly discovered, changed,
/// removed, or left unchanged since the previous run.
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
    public static readonly WebCrawlerReindexResult Empty = new(0, 0, 0, 0);
}
