using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes;

/// <summary>
/// Base YesSql map index that stores the unique item identifier, enabling efficient
/// catalog lookups by <see cref="ItemId"/> across all derived index types.
/// </summary>
public abstract class CatalogItemIndex : MapIndex
{
    /// <summary>
    /// Gets or sets the unique identifier of the catalogued item.
    /// </summary>
    public string ItemId { get; set; }
}
