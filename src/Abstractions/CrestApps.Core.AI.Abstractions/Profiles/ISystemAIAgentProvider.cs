using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// Contributes system (virtual) AI agent profiles that are not persisted in the profile
/// store. System agents are merged into <see cref="IAIProfileManager.GetAsync(AIProfileType, System.Threading.CancellationToken)"/>
/// results for <see cref="AIProfileType.Agent"/>, so they are automatically discoverable by
/// the orchestrator, invocable as tools, and exposed through the Agent-to-Agent (A2A) host,
/// while remaining read-only and hidden from the user-facing agent selection list.
/// </summary>
public interface ISystemAIAgentProvider
{
    /// <summary>
    /// Gets the system agent profiles contributed by this provider.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read-only list of system agent profiles.</returns>
    ValueTask<IReadOnlyList<AIProfile>> GetAgentsAsync(CancellationToken cancellationToken = default);
}
