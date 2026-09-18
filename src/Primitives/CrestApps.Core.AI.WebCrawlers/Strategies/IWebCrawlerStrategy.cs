using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.WebCrawlers.Strategies;

/// <summary>
/// A pluggable web-crawl strategy. A strategy defines how a site is discovered (which pages exist and
/// when they changed) and how a single page is fetched and cleaned. Implementations are registered keyed
/// by <see cref="Name"/>. Today the only built-in strategy is sitemap discovery; future strategies (for
/// example depth-limited link following) plug in without changing the data source or the UI.
/// </summary>
public interface IWebCrawlerStrategy
{
    /// <summary>
    /// Gets the strategy identifier (for example <c>Sitemap</c>).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Validates the crawler's strategy-specific settings.
    /// </summary>
    /// <param name="crawler">The crawler to validate.</param>
    /// <param name="result">The validation result collector.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask ValidateAsync(WebCrawler crawler, ValidationResultDetails result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers the current set of pages for the crawler, together with their change metadata.
    /// </summary>
    /// <param name="crawler">The crawler configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The discovered pages.</returns>
    Task<IReadOnlyList<CrawledPageRef>> DiscoverAsync(WebCrawler crawler, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers the current set of pages and says whether that is all of them.
    /// </summary>
    /// <param name="crawler">The crawler configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The pages, and whether the discovery covered the whole site.</returns>
    /// <remarks>
    /// Deleting a page because it was missing from a crawl is only sound when the crawl saw the whole site.
    /// A strategy that stopped short - a page cap, an unreachable sitemap, a throttled host - says so here,
    /// and nothing is removed on the strength of it.
    /// <para>
    /// Default-implemented in terms of <see cref="DiscoverAsync"/>, so a strategy outside this repository
    /// keeps working and keeps today's behaviour: a non-empty crawl is treated as complete.
    /// </para>
    /// </remarks>
    async Task<WebCrawlDiscovery> DiscoverDetailedAsync(WebCrawler crawler, CancellationToken cancellationToken = default)
    {
        var pages = await DiscoverAsync(crawler, cancellationToken);

        return new WebCrawlDiscovery(pages ?? [], pages is { Count: > 0 });
    }

    /// <summary>
    /// Fetches and cleans a single page.
    /// </summary>
    /// <param name="crawler">The crawler configuration.</param>
    /// <param name="url">The page URL to fetch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cleaned page, or <see langword="null"/> when the page could not be fetched or was empty.</returns>
    Task<CrawledPage> FetchAsync(WebCrawler crawler, string url, CancellationToken cancellationToken = default);
}
