using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents one configured web crawler: a scraping strategy identified by its
/// <see cref="CrestApps.Core.Models.SourceCatalogEntry.Source"/> (for example <c>Sitemap</c>), the
/// strategy's settings (stored in <see cref="CrestApps.Core.ExtensibleEntity.Properties"/>), and the target
/// <see cref="IngestionSource.AIDataSourceId"/> whose knowledge base the scraped pages are indexed into.
/// Many crawlers can point at a single <c>Web</c> AI data source.
/// </summary>
/// <remarks>
/// A crawler reads a website. A folder or a file server is a <see cref="FileSource"/>, which is a separate
/// kind of record in a separate store: the two share an <see cref="IngestionSource"/> shape and the
/// connector contract, and nothing else.
/// </remarks>
public sealed class WebCrawler : IngestionSource, ICloneable<WebCrawler>
{
    /// <summary>
    /// Clones the crawler.
    /// </summary>
    public WebCrawler Clone()
    {
        var clone = new WebCrawler();

        CopyTo(clone);

        return clone;
    }
}
