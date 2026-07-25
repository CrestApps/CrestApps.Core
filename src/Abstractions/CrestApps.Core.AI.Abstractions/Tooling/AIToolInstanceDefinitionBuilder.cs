using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// A fluent builder for configuring the display metadata of a registered
/// <see cref="IAIToolInstanceDefinition"/>.
/// </summary>
/// <typeparam name="TDefinition">The definition type implementing <see cref="IAIToolInstanceDefinition"/>.</typeparam>
public sealed class AIToolInstanceDefinitionBuilder<TDefinition>
    where TDefinition : class, IAIToolInstanceDefinition
{
    private readonly AIToolInstanceDefinitionEntry _entry;

    internal AIToolInstanceDefinitionBuilder(AIToolInstanceDefinitionEntry entry)
    {
        _entry = entry;
    }

    /// <summary>
    /// Sets the friendly display name shown when choosing this definition.
    /// </summary>
    /// <param name="displayName">The localized display name.</param>
    public AIToolInstanceDefinitionBuilder<TDefinition> WithDisplayName(LocalizedString displayName)
    {
        _entry.DisplayName = displayName;

        return this;
    }

    /// <summary>
    /// Sets the description that explains what the definition does.
    /// </summary>
    /// <param name="description">The localized description.</param>
    public AIToolInstanceDefinitionBuilder<TDefinition> WithDescription(LocalizedString description)
    {
        _entry.Description = description;

        return this;
    }

    /// <summary>
    /// Sets the category used to group this definition in the UI.
    /// </summary>
    /// <param name="category">The category.</param>
    public AIToolInstanceDefinitionBuilder<TDefinition> WithCategory(string category)
    {
        _entry.Category = category;

        return this;
    }
}
