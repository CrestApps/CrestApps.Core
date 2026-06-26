namespace CrestApps.Core.AI.Models;

/// <summary>
/// Metadata for AI profiles with <see cref="AIProfileType.Agent"/> type.
/// Stored in the profile's Properties via Put/As pattern.
/// </summary>
public sealed class AgentMetadata
{
    /// <summary>
    /// Gets or sets the availability mode for this agent.
    /// <see cref="AgentAvailability.OnDemand"/> agents are included only when matched
    /// by semantic or keyword scoring. <see cref="AgentAvailability.AlwaysAvailable"/> agents
    /// are automatically included in every completion request and are not shown in the
    /// user-selectable agent list.
    /// </summary>
    public AgentAvailability Availability { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this agent may execute its own tools when
    /// invoked as a sub-agent. By default sub-agents run with tools disabled to prevent
    /// runaway recursion. When set to <see langword="true"/>, the agent runs through the
    /// orchestrator with its configured tools enabled, guarded by a recursion-depth limit
    /// that prevents the agent from invoking other agents.
    /// </summary>
    public bool AllowToolInvocation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this agent is a system (virtual) agent
    /// contributed by an <c>ISystemAIAgentProvider</c> rather than a stored profile.
    /// System agents are always available to the model and exposed through A2A, but are not
    /// editable and are hidden from the user-facing agent selection list.
    /// </summary>
    public bool IsSystem { get; set; }
}
