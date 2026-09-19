using CrestApps.Core.AI.A2A.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.A2A;

/// <summary>
/// YesSql map index for <see cref="A2AConnection"/>, storing the item identifier
/// and display text to support efficient catalog queries.
/// </summary>
public sealed class A2AConnectionIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the human-readable display text of the A2A connection.
    /// </summary>
    public string DisplayText { get; set; }
}
