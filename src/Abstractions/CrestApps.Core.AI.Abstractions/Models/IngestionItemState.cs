using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Tracks what one ingestion run knows about a single item it read, so the next run can tell what is new,
/// what changed and what is gone without re-reading everything.
/// </summary>
/// <remarks>
/// The owning record's identifier is stored in <see cref="SourceCatalogEntry.Source"/> so every item of one
/// source can be read through the source-catalog abstraction.
/// <para>
/// This was once kept in <see cref="WebCrawlState"/>, where a file's path lived in a field called
/// <c>Url</c>, a connector's opaque change token in one called <c>ChangeFrequency</c>, and the identifier of
/// the ingested document in one called <c>ContentHash</c>. The names here say what the values are.
/// </para>
/// </remarks>
public sealed class IngestionItemState : SourceCatalogEntry
{
    /// <summary>
    /// Gets or sets what the connector knows the item by, stable across runs. A path for a folder, a remote
    /// path for a file server, a URL for a crawl.
    /// </summary>
    public string ItemKey { get; set; }

    /// <summary>
    /// Gets or sets the opaque value the connector last reported for this item. Compared verbatim, never
    /// parsed: it is an ETag for one source and a size and timestamp for another.
    /// </summary>
    public string ChangeToken { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the knowledge document this item last produced, which is what a
    /// removal has to name.
    /// </summary>
    public string DocumentRootId { get; set; }

    /// <summary>
    /// Gets or sets the last-modified timestamp the source reported at the last ingestion, or
    /// <see langword="null"/> when it reported none.
    /// </summary>
    public DateTime? LastModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets how large the item was at the last ingestion, when the source knew.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the item was last read and ingested.
    /// </summary>
    public DateTime LastIngestedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the item was last seen in a listing. An item missing from a
    /// complete listing is treated as removed.
    /// </summary>
    public DateTime LastSeenUtc { get; set; }
}
