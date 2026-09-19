using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.WebCrawlers.Strategies;

namespace CrestApps.Core.Mvc.Web.Areas.WebCrawlers.Services;

/// <summary>
/// Decides which records the Web Crawlers screens can act on: those whose crawl strategy is still registered.
/// </summary>
/// <remarks>
/// File sources are their own records in their own store, so this no longer has to tell the two kinds apart.
/// What is left is the case it always also covered: whatever registered a strategy may be gone, and a record
/// naming one cannot be run or meaningfully edited.
/// </remarks>
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

        // A source naming no registered strategy is left out: whatever registered it is gone, and the
        // record cannot be run either way.
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
