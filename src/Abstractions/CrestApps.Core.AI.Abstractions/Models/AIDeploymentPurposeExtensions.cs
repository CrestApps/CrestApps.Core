namespace CrestApps.Core.AI.Models;

/// <summary>
/// Extension methods for the <see cref="AIDeploymentPurpose"/> flags enum.
/// </summary>
public static class AIDeploymentPurposeExtensions
{
    private static readonly AIDeploymentPurpose _allSupportedPurposes = Enum.GetValues<AIDeploymentPurpose>()
        .Where(static purpose => purpose != AIDeploymentPurpose.None)
        .Aggregate(AIDeploymentPurpose.None, static (current, purpose) => current | purpose);

    /// <summary>
    /// Determines whether the specified purpose is supported.
    /// </summary>
    /// <param name="value">The selected purposes.</param>
    /// <param name="purpose">The purpose to test.</param>
    public static bool Supports(this AIDeploymentPurpose value, AIDeploymentPurpose purpose)
    {
        return purpose != AIDeploymentPurpose.None && (value & purpose) == purpose;
    }

    /// <summary>
    /// Determines whether the specified purpose selection is valid.
    /// </summary>
    /// <param name="value">The selected purposes.</param>
    public static bool IsValidSelection(this AIDeploymentPurpose value)
    {
        return value != AIDeploymentPurpose.None && (value & ~_allSupportedPurposes) == 0;
    }

    /// <summary>
    /// Gets the supported purposes.
    /// </summary>
    /// <param name="value">The selected purposes.</param>
    public static IEnumerable<AIDeploymentPurpose> GetSupportedPurposes(this AIDeploymentPurpose value)
    {
        return Enum.GetValues<AIDeploymentPurpose>().Where(purpose => value.Supports(purpose));
    }

    /// <summary>
    /// Converts a legacy deployment type value to the purpose representation.
    /// </summary>
    /// <param name="type">The legacy type.</param>
#pragma warning disable CS0618
    public static AIDeploymentPurpose ToPurpose(this AIDeploymentType type)
    {
        return type switch
        {
            AIDeploymentType.None => AIDeploymentPurpose.None,
            _ => (AIDeploymentPurpose)(int)type,
        };
    }
#pragma warning restore CS0618

    /// <summary>
    /// Converts a purpose value to the legacy deployment type representation.
    /// Unsupported purposes such as <see cref="AIDeploymentPurpose.Vision"/> are omitted.
    /// </summary>
    /// <param name="purpose">The purpose.</param>
#pragma warning disable CS0618
    public static AIDeploymentType ToLegacyType(this AIDeploymentPurpose purpose)
    {
        return (AIDeploymentType)((int)purpose & (int)(
            AIDeploymentPurpose.Chat |
            AIDeploymentPurpose.Utility |
            AIDeploymentPurpose.Embedding |
            AIDeploymentPurpose.Image |
            AIDeploymentPurpose.SpeechToText |
            AIDeploymentPurpose.TextToSpeech));
    }
#pragma warning restore CS0618

}
