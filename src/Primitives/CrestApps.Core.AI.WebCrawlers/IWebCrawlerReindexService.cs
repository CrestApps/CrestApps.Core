namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// Orchestrates re-indexing across every configured web crawler. This is the reusable unit of work driven
/// by <see cref="WebCrawlerReindexBackgroundService"/>; hosts that prefer their own scheduling (or a manual
/// trigger) can invoke it directly instead.
/// </summary>
public interface IWebCrawlerReindexService
{
    /// <summary>
    /// Re-indexes every enabled crawler that is due per its re-index interval.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ReindexDueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-indexes every enabled crawler immediately, regardless of its re-index interval.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ReindexAllAsync(CancellationToken cancellationToken = default);
}
