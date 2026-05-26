namespace CrestApps.Core.AI.Models;

/// <summary>
/// Legacy extension methods for the <see cref="AIDeploymentType"/> flags enum.
/// Use <see cref="AIDeploymentPurposeExtensions"/> for new code.
/// </summary>
[Obsolete("Use AIDeploymentPurposeExtensions instead.")]
public static class AIDeploymentTypeExtensions
{
    /// <summary>
    /// Determines whether the condition is met.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="type">The type.</param>
    public static bool Supports(this AIDeploymentType value, AIDeploymentType type)
    {
        return value.ToPurpose().Supports(type.ToPurpose());
    }

    /// <summary>
    /// Determines whether valid selection.
    /// </summary>
    /// <param name="value">The value.</param>
    public static bool IsValidSelection(this AIDeploymentType value)
    {
        return value.ToPurpose().IsValidSelection();
    }

    /// <summary>
    /// Gets supported types.
    /// </summary>
    /// <param name="value">The value.</param>
    public static IEnumerable<AIDeploymentType> GetSupportedTypes(this AIDeploymentType value)
    {
        return value.ToPurpose().GetSupportedPurposes().Select(static purpose => purpose.ToLegacyType());
    }
}
