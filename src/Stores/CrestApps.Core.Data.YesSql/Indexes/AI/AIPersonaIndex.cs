using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql map index for <see cref="AIPersona"/>, storing the item identifier and unique name to support efficient
/// catalog queries.
/// </summary>
public sealed class AIPersonaIndex : CatalogItemIndex, INameAwareIndex
{
    /// <summary>
    /// Gets or sets the unique technical name of the AI persona.
    /// </summary>
    public string Name { get; set; }
}
