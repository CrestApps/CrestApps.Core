using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Describes the display metadata for a registered <see cref="IAIToolInstanceDefinition"/>. This
/// metadata drives the management UI that lets users pick a definition and create instances of it.
/// </summary>
public sealed class AIToolInstanceDefinitionEntry
{
    /// <summary>
    /// Gets the registered name of the definition. Matches <see cref="IAIToolInstanceDefinition.Name"/>.
    /// </summary>
    public string Name { get; internal set; }

    /// <summary>
    /// Gets or sets the friendly display name shown when choosing a definition to instantiate.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description that explains what kinds of instances the definition produces.
    /// </summary>
    public LocalizedString Description { get; set; }

    /// <summary>
    /// Gets or sets an optional category used to group definitions in the UI.
    /// </summary>
    public string Category { get; set; }
}
