namespace CrestApps.Core.Models;

/// <summary>
/// Base catalog item that participates in the extensible entity system.
/// Every item stored in a catalog must derive from this class.
/// </summary>
public class CatalogItem : ExtensibleEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for the catalog item.
    /// </summary>
    public string ItemId { get; set; }
}
