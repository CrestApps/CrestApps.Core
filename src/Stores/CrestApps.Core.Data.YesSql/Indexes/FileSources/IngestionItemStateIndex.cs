using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.FileSources;

/// <summary>
/// YesSql map index for <see cref="IngestionItemState"/>, keyed by the owning record (its source) so a run
/// can read everything it recorded last time in one query.
/// </summary>
public sealed class IngestionItemStateIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the owning record's identifier (the state record's source).
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets what the connector knows the item by.
    /// </summary>
    public string ItemKey { get; set; }
}
