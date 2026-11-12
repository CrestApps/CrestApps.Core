namespace CrestApps.Core.AI.Models;

/// <summary>
/// The tool calls an assistant message made, stored on the message (with <c>Put</c>). The conversation history
/// replays the completed calls, and a run resumes the calls that wait for an approval or for the client.
/// </summary>
public sealed class ChatTurnToolActivity
{
    /// <summary>
    /// Gets or sets the tool calls, in the order the model made them.
    /// </summary>
    public List<ChatTurnToolCall> ToolCalls { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the turn ended because a tool opened another page, and waits for the
    /// client to continue it on that page. The continuation clears it, so a turn is continued only once.
    /// </summary>
    public bool ContinuesOnNextPage { get; set; }

    /// <summary>
    /// Gets a value indicating whether a call waits for an approval or for the client's result.
    /// </summary>
    public bool HasPendingCalls => ToolCalls.Any(call => call.Status is ChatTurnToolCallStatus.AwaitingApproval or ChatTurnToolCallStatus.AwaitingClient);
}
