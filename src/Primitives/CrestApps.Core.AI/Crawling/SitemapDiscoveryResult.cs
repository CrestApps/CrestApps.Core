namespace CrestApps.Core.AI.Crawling;

/// <summary>
/// What one walk of a sitemap graph found, and whether it found everything.
/// </summary>
/// <param name="Entries">The discovered page entries, de-duplicated by URL.</param>
/// <param name="IsComplete">
/// Whether the walk covered the whole graph. <see langword="false"/> when the page cap was reached, the
/// graph was larger than the crawler follows, or a sitemap could not be downloaded.
/// </param>
/// <param name="Message">Why the walk stopped short, when it did.</param>
public sealed record SitemapDiscoveryResult(
    IReadOnlyList<SitemapEntry> Entries,
    bool IsComplete,
    string Message = null);
