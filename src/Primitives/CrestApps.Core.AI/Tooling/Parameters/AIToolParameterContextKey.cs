namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// Describes a context key an <see cref="IAIToolParameterContextResolver"/> can resolve, so the
/// management UI can offer the available keys as a list instead of asking users to type an identifier
/// they have no way to discover.
/// </summary>
/// <param name="Key">The key stored in <see cref="AIToolInstanceParameter.ContextKey"/>.</param>
/// <param name="DisplayName">The friendly name shown in the management UI.</param>
/// <param name="Description">An optional explanation of what the key resolves to.</param>
public readonly record struct AIToolParameterContextKey(string Key, string DisplayName, string Description = null);
