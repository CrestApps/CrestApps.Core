using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// What every configured ingestion source has in common: a named <see cref="SourceCatalogEntry.Source"/>
/// that decides how it is read, the data source it fills, and when it is read again.
/// </summary>
/// <remarks>
/// A website and an FTP server are not the same thing and are not stored together, but an
/// <see cref="CrestApps.Core.AI.Indexing.IIngestionConnector"/> does not need to know which it was handed:
/// it lists what is there and fetches one item. This base is what it is handed, so a connector can be
/// written once and a new kind of source added without touching it.
/// </remarks>
public abstract class IngestionSource : SourceCatalogEntry, IDisplayTextAwareModel, IModifiedUtcAwareModel
{
    /// <summary>
    /// Gets or sets the human-readable display name.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the AI data source whose knowledge base receives what is read.
    /// </summary>
    public string AIDataSourceId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this source is active. A disabled source is skipped by the
    /// background services and is not read during a full data source synchronization.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets how often, in minutes, this source is read again. When not set, the global default is
    /// used.
    /// </summary>
    public int? ReindexIntervalMinutes { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this source was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this source was last modified.
    /// </summary>
    public DateTime? ModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who created this source.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the owner identifier associated with this source.
    /// </summary>
    public string OwnerId { get; set; }

    /// <summary>
    /// Copies the members declared here onto another instance.
    /// </summary>
    /// <param name="target">The instance to copy onto.</param>
    /// <remarks>
    /// Cloning is declared per concrete type, so this exists to keep every clone from having to repeat the
    /// shared members and quietly drop one that is added later. It is public because a record also moves
    /// between kinds: a file source stored as a web crawler before the two were separated is copied across
    /// with it.
    /// </remarks>
    public void CopyTo(IngestionSource target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.ItemId = ItemId;
        target.Source = Source;
        target.DisplayText = DisplayText;
        target.AIDataSourceId = AIDataSourceId;
        target.Enabled = Enabled;
        target.ReindexIntervalMinutes = ReindexIntervalMinutes;
        target.CreatedUtc = CreatedUtc;
        target.ModifiedUtc = ModifiedUtc;
        target.Author = Author;
        target.OwnerId = OwnerId;
        target.Properties = Properties.Clone();
    }
}
