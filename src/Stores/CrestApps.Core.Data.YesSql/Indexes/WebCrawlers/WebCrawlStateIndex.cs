using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.WebCrawlers;

/// <summary>
/// YesSql map index for <see cref="WebCrawlState"/>, keyed by the owning crawler (its source) so the
/// re-index service can efficiently read every recorded page for a crawler.
/// </summary>
public sealed class WebCrawlStateIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the owning crawler identifier (the crawl-state record's source).
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the scraped page URL.
    /// </summary>
    public string Url { get; set; }
}
