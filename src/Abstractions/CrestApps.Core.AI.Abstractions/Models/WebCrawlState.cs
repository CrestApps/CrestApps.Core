using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Tracks the last-known crawl state of a single scraped page for one <see cref="WebCrawler"/>. The
/// re-index service compares a fresh crawl against these records to decide which pages are new, changed,
/// or removed, so only affected pages are re-fetched and re-embedded. The owning crawler's identifier is
/// stored in <see cref="CrestApps.Core.Models.SourceCatalogEntry.Source"/> so the records can be queried
/// through the source-catalog abstraction.
/// </summary>
public sealed class WebCrawlState : SourceCatalogEntry
{
    /// <summary>
    /// Gets or sets the canonical, absolute URL of the scraped page. This is the reference id used for the
    /// knowledge-base chunks and the page's citation link.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the last-modified timestamp advertised for the page the last time it was indexed, or
    /// <see langword="null"/> when the crawl did not report one.
    /// </summary>
    public DateTime? LastModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the change-frequency hint advertised for the page, or <see langword="null"/> when the
    /// crawl did not report one.
    /// </summary>
    public string ChangeFrequency { get; set; }

    /// <summary>
    /// Gets or sets a hash of the cleaned page content captured at the last index.
    /// </summary>
    public string ContentHash { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the page was last fetched and indexed.
    /// </summary>
    public DateTime LastIndexedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the page was last seen during a crawl. A page missing since the
    /// previous crawl is treated as removed.
    /// </summary>
    public DateTime LastSeenUtc { get; set; }
}
