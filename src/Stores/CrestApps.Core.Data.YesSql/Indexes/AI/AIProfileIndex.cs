using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql map index for <see cref="AIProfile"/>, storing the item identifier,
/// unique name, source, and type to support efficient catalog queries.
/// </summary>
public sealed class AIProfileIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    /// <summary>
    /// Gets or sets the unique technical name of the AI profile.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the source or provider name of the AI profile.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the profile type discriminator.
    /// </summary>
    public string Type { get; set; }
}
