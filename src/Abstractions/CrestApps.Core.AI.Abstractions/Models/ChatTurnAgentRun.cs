namespace CrestApps.Core.AI.Models;

/// <summary>
/// The run of an agent that paused because a tool call it made waits for a person's approval. It is stored with
/// the assistant message that called the agent (in <see cref="ChatTurnToolActivity.AgentRuns"/>), so the run that
/// resumes continues the agent where it stopped instead of starting it again.
/// </summary>
public sealed class ChatTurnAgentRun
{
    /// <summary>
    /// The most characters of a tool result kept with the run. A longer result is cut, and the agent reads the cut
    /// result when it resumes.
    /// </summary>
    public const int MaxToolResultLength = 16000;

    /// <summary>
    /// Gets or sets the id of the assistant's call to the agent.
    /// </summary>
    public string CallId { get; set; }

    /// <summary>
    /// Gets or sets the name of the agent.
    /// </summary>
    public string AgentName { get; set; }

    /// <summary>
    /// Gets or sets the tool calls the agent made, in order: the calls that ran, with their results, and the calls
    /// that wait for an approval. A call that ran is never made again when the agent resumes.
    /// </summary>
    public List<ChatTurnToolCall> ToolCalls { get; set; } = [];

    /// <summary>
    /// Gets a value indicating whether a call of the agent waits for an approval.
    /// </summary>
    public bool HasPendingCalls => ToolCalls.Any(call => call.Status == ChatTurnToolCallStatus.AwaitingApproval);
}
