using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.Data.YesSql.Indexes.Tooling;

/// <summary>
/// YesSql map index for <see cref="AIToolInstance"/>, storing the item identifier, unique name, and
/// source to support efficient tool instance queries.
/// </summary>
public sealed class AIToolInstanceIndex : CatalogItemIndex, ISourceAwareIndex, INameAwareIndex
{
    /// <summary>
    /// Gets or sets the unique technical name of the tool instance.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the source, i.e. the tool instance source name.
    /// </summary>
    public string Source { get; set; }
}
