namespace CrestApps.Core.AI.WebCrawlers.Strategies;

/// <summary>
/// What one discovery pass found, and whether it found everything.
/// </summary>
/// <param name="Pages">The discovered pages.</param>
/// <param name="IsComplete">
/// Whether the pass covered the whole site. A page missing from an incomplete pass is missing because the
/// crawl stopped short, not because the site removed it.
/// </param>
/// <param name="Message">Why the pass stopped short, when it did.</param>
public sealed record WebCrawlDiscovery(
    IReadOnlyList<CrawledPageRef> Pages,
    bool IsComplete,
    string Message = null);
