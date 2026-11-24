namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// The names of the additional properties a tool approval request
/// (<see cref="Microsoft.Extensions.AI.ToolApprovalRequestContent"/>) carries when the call that waits for the
/// approval was made by an agent the assistant delegated to, not by the assistant itself. The orchestrator adds
/// such a request to the assistant's stream, and whatever shows the stream to a person reads these properties to
/// name the agent and to keep the agent's paused run for the resume.
/// </summary>
public static class AgentToolApprovalProperties
{
    /// <summary>
    /// The key of the assistant's call to the agent (a <see cref="Microsoft.Extensions.AI.FunctionCallContent"/>):
    /// its call id, the agent's name, and the arguments the assistant called the agent with.
    /// </summary>
    public const string AgentCall = "crestapps.agent_call";

    /// <summary>
    /// The key of the agent's paused run (a <see cref="Models.ChatTurnAgentRun"/>).
    /// </summary>
    public const string AgentRun = "crestapps.agent_run";
}
