using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.WebCrawlers;

/// <summary>
/// YesSql map index for <see cref="WebCrawler"/>, keyed by the target data source so the source handler
/// and re-index service can find every crawler for a <c>Web</c> data source.
/// </summary>
public sealed class WebCrawlerIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the human-readable display text of the crawler.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the target AI data source identifier.
    /// </summary>
    public string AIDataSourceId { get; set; }

    /// <summary>
    /// Gets or sets the crawl strategy identifier (the crawler's source).
    /// </summary>
    public string Source { get; set; }
}
