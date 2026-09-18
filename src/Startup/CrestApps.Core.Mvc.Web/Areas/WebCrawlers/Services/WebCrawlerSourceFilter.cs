using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.WebCrawlers.Strategies;

namespace CrestApps.Core.Mvc.Web.Areas.WebCrawlers.Services;

/// <summary>
/// Decides which records belong on the Web Crawlers screens. A record's source is either a crawl strategy or
/// an ingestion connector, and that is the only thing separating the two kinds: a strategy-backed record
/// crawls a website, while a connector-backed record reads files and is managed on the File Sources screens.
/// </summary>
public static class WebCrawlerSourceFilter
{
    /// <summary>
    /// Determines whether a source names a registered crawl strategy.
    /// </summary>
    /// <param name="source">The record's source.</param>
    /// <param name="strategies">The registered crawl strategies.</param>
    /// <returns><c>true</c> when the source is a registered crawl strategy; otherwise, <c>false</c>.</returns>
    public static bool IsCrawlStrategy(string source, IReadOnlyList<WebCrawlerStrategyDescriptor> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        // An unregistered source belongs to neither screen, so it is left out rather than shown here by
        // default. Whatever registered it is gone, and the record cannot be run either way.
        foreach (var strategy in strategies)
        {
            if (string.Equals(strategy.Strategy, source, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Keeps only the records the Web Crawlers screens manage.
    /// </summary>
    /// <param name="crawlers">The records to filter.</param>
    /// <param name="strategies">The registered crawl strategies.</param>
    /// <returns>The records whose source is a registered crawl strategy.</returns>
    public static IReadOnlyList<WebCrawler> SelectCrawlStrategyRecords(IEnumerable<WebCrawler> crawlers, IReadOnlyList<WebCrawlerStrategyDescriptor> strategies)
    {
        ArgumentNullException.ThrowIfNull(crawlers);
        ArgumentNullException.ThrowIfNull(strategies);

        return crawlers
            .Where(crawler => IsCrawlStrategy(crawler.Source, strategies))
            .ToArray();
    }
}
