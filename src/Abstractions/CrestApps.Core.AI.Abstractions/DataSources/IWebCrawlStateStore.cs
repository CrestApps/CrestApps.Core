using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.DataSources;

/// <summary>
/// Persists the per-page crawl state (<see cref="WebCrawlState"/>) for web crawlers. The re-index service
/// uses it to diff a fresh crawl against the pages already indexed. Records are grouped by the owning
/// crawler, which is stored as their source, so <see cref="ISourceCatalog{T}.GetAsync(string, System.Threading.CancellationToken)"/>
/// retrieves every page for a crawler.
/// </summary>
public interface IWebCrawlStateStore : ISourceCatalog<WebCrawlState>
{
    /// <summary>
    /// Deletes every crawl-state record for the specified crawler.
    /// </summary>
    /// <param name="webCrawlerId">The owning crawler identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteByCrawlerIdAsync(string webCrawlerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the crawl-state records for the specified page URLs within a crawler.
    /// </summary>
    /// <param name="webCrawlerId">The owning crawler identifier.</param>
    /// <param name="urls">The page URLs whose crawl state should be removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteByUrlsAsync(string webCrawlerId, IEnumerable<string> urls, CancellationToken cancellationToken = default);
}
