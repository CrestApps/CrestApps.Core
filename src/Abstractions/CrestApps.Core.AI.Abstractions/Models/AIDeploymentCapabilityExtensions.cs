namespace CrestApps.Core.AI.Models;

/// <summary>
/// Extension methods for the <see cref="AIDeploymentCapability"/> flags enum.
/// </summary>
public static class AIDeploymentCapabilityExtensions
{
    private static readonly AIDeploymentCapability _allSupportedCapabilities = Enum.GetValues<AIDeploymentCapability>()
        .Where(static capability => capability != AIDeploymentCapability.None)
        .Aggregate(AIDeploymentCapability.None, static (current, capability) => current | capability);

    /// <summary>
    /// Determines whether the specified capability is supported.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="capability">The capability.</param>
    public static bool Supports(this AIDeploymentCapability value, AIDeploymentCapability capability)
    {
        return capability != AIDeploymentCapability.None && (value & capability) == capability;
    }

    /// <summary>
    /// Determines whether the specified capability selection is valid.
    /// </summary>
    /// <param name="value">The value.</param>
    public static bool IsValidSelection(this AIDeploymentCapability value)
    {
        return value != AIDeploymentCapability.None && (value & ~_allSupportedCapabilities) == 0;
    }

    /// <summary>
    /// Gets the supported capabilities.
    /// </summary>
    /// <param name="value">The value.</param>
    public static IEnumerable<AIDeploymentCapability> GetSupportedCapabilities(this AIDeploymentCapability value)
    {
        return Enum.GetValues<AIDeploymentCapability>().Where(capability => value.Supports(capability));
    }

    /// <summary>
    /// Converts a legacy deployment type value to the capability representation.
    /// </summary>
    /// <param name="type">The legacy type.</param>
#pragma warning disable CS0618 // Type or member is obsolete
    public static AIDeploymentCapability ToCapability(this AIDeploymentType type)
    {
        return type switch
        {
            AIDeploymentType.None => AIDeploymentCapability.None,
            _ => (AIDeploymentCapability)(int)type,
        };
    }
#pragma warning restore CS0618 // Type or member is obsolete

    /// <summary>
    /// Converts a capability value to the legacy deployment type representation.
    /// Unsupported capabilities such as <see cref="AIDeploymentCapability.Vision"/> are omitted.
    /// </summary>
    /// <param name="capability">The capability.</param>
#pragma warning disable CS0618 // Type or member is obsolete
    public static AIDeploymentType ToLegacyType(this AIDeploymentCapability capability)
    {
        return (AIDeploymentType)((int)capability & (int)(
            AIDeploymentCapability.Chat |
            AIDeploymentCapability.Utility |
            AIDeploymentCapability.Embedding |
            AIDeploymentCapability.Image |
            AIDeploymentCapability.SpeechToText |
            AIDeploymentCapability.TextToSpeech));
    }
#pragma warning restore CS0618 // Type or member is obsolete
}
