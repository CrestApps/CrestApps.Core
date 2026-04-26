namespace CrestApps.Core.AI.Models;

/// <summary>
/// Extension methods for the <see cref="AIDeploymentType"/> flags enum.
/// </summary>
public static class AIDeploymentTypeExtensions
{
    private static readonly AIDeploymentType _allSupportedTypes = Enum.GetValues<AIDeploymentType>().Where(type => type != AIDeploymentType.None).Aggregate(AIDeploymentType.None, static (current, type) => current | type);

    /// <summary>
    /// Determines whether the condition is met.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="type">The type.</param>
    public static bool Supports(this AIDeploymentType value, AIDeploymentType type)
    {
        return type != AIDeploymentType.None && (value & type) == type;
    }

    /// <summary>
    /// Determines whether valid selection.
    /// </summary>
    /// <param name="value">The value.</param>
    public static bool IsValidSelection(this AIDeploymentType value)
    {
        return value != AIDeploymentType.None && (value & ~_allSupportedTypes) == 0;
    }

    /// <summary>
    /// Gets supported types.
    /// </summary>
    /// <param name="value">The value.</param>
    public static IEnumerable<AIDeploymentType> GetSupportedTypes(this AIDeploymentType value)
    {
        return Enum.GetValues<AIDeploymentType>().Where(type => value.Supports(type));
    }
}
