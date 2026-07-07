namespace CrestApps.Core.AI.Models;

/// <summary>
/// Extension helpers for inspecting <see cref="AIProfile"/> instances of type
/// <see cref="AIProfileType.Agent"/> and their <see cref="AgentMetadata"/>.
/// </summary>
public static class AIAgentProfileExtensions
{
    /// <summary>
    /// Determines whether the agent profile is always available — automatically included in
    /// every orchestration request rather than being explicitly selected.
    /// </summary>
    /// <param name="profile">The agent profile.</param>
    /// <returns><see langword="true"/> when the agent is always available; otherwise <see langword="false"/>.</returns>
    public static bool IsAlwaysAvailableAgent(this AIProfile profile)
    {
        return profile is not null
            && profile.TryGet<AgentMetadata>(out var metadata)
            && metadata.Availability == AgentAvailability.AlwaysAvailable;
    }

    /// <summary>
    /// Determines whether the agent profile is a system (virtual) agent contributed
    /// in code rather than persisted in the profile store.
    /// </summary>
    /// <param name="profile">The agent profile.</param>
    /// <returns><see langword="true"/> when the agent is a system agent; otherwise <see langword="false"/>.</returns>
    public static bool IsSystemAgent(this AIProfile profile)
    {
        return profile is not null
            && profile.TryGet<AgentMetadata>(out var metadata)
            && metadata.IsSystem;
    }

    /// <summary>
    /// Determines whether the agent profile should appear in the user-facing agent selection
    /// list. Always-available agents and system agents are excluded because they are
    /// included automatically or managed in code and never need to be selected manually.
    /// </summary>
    /// <param name="profile">The agent profile.</param>
    /// <returns><see langword="true"/> when the agent is user-selectable; otherwise <see langword="false"/>.</returns>
    public static bool IsUserSelectableAgent(this AIProfile profile)
    {
        return profile is not null
            && !string.IsNullOrEmpty(profile.Description)
            && !profile.IsAlwaysAvailableAgent()
            && !profile.IsSystemAgent();
    }
}
