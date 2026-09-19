using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql map index for <see cref="AIDeployment"/>, storing the item identifier,
/// unique name, and source to support efficient catalog queries.
/// </summary>
public sealed class AIDeploymentIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    /// <summary>
    /// Gets or sets the unique technical name of the AI deployment.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the source or provider name of the AI deployment.
    /// </summary>
    public string Source { get; set; }
}
