using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents one configured web crawler: a scraping strategy identified by its <see cref="Source"/> (for
/// example <c>Sitemap</c>), the strategy's settings (stored in <see cref="ExtensibleEntity.Properties"/>),
/// and the target <see cref="AIDataSourceId"/> whose knowledge base the scraped pages are indexed into.
/// Many crawlers can point at a single <c>Web</c> AI data source.
/// </summary>
public sealed class WebCrawler : SourceCatalogEntry, IDisplayTextAwareModel, IModifiedUtcAwareModel, ICloneable<WebCrawler>
{
    /// <summary>
    /// Gets or sets the human-readable display name for this crawler.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the target <c>Web</c> AI data source whose knowledge base receives
    /// the scraped pages.
    /// </summary>
    public string AIDataSourceId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this crawler is active. Disabled crawlers are skipped by the
    /// re-index background service and are not read during a full data source synchronization.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets how often, in minutes, the background service re-crawls this site and re-indexes the
    /// pages that changed. When not set, the global default is used.
    /// </summary>
    public int? ReindexIntervalMinutes { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this crawler was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this crawler was last modified.
    /// </summary>
    public DateTime? ModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who created this crawler.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the owner identifier associated with this crawler.
    /// </summary>
    public string OwnerId { get; set; }

    /// <summary>
    /// Clones the crawler.
    /// </summary>
    public WebCrawler Clone()
    {
        return new WebCrawler
        {
            ItemId = ItemId,
            DisplayText = DisplayText,
            AIDataSourceId = AIDataSourceId,
            Source = Source,
            Enabled = Enabled,
            ReindexIntervalMinutes = ReindexIntervalMinutes,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = ModifiedUtc,
            Author = Author,
            OwnerId = OwnerId,
            Properties = Properties.Clone(),
        };
    }
}
