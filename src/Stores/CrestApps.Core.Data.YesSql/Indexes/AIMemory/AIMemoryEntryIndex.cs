using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.AIMemory;

/// <summary>
/// YesSql map index for <see cref="AIMemoryEntry"/>, storing the item identifier,
/// owning user, and entry name to support efficient memory lookups.
/// </summary>
public sealed class AIMemoryEntryIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the identifier of the user who owns this memory entry.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets the unique technical name of the memory entry.
    /// </summary>
    public string Name { get; set; }
}
