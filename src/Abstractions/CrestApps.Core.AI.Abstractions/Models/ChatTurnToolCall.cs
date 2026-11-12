namespace CrestApps.Core.AI.Models;

/// <summary>
/// One tool call an assistant message made, kept with the message so the history can replay it, the chat can show
/// it, and a call waiting for an approval or for the client can be resumed.
/// </summary>
public sealed class ChatTurnToolCall
{
    /// <summary>
    /// Gets or sets the id of the call, as the model gave it.
    /// </summary>
    public string CallId { get; set; }

    /// <summary>
    /// Gets or sets the name of the tool.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the arguments, as a JSON object.
    /// </summary>
    public string Arguments { get; set; }

    /// <summary>
    /// Gets or sets the result, as text, once the call has one.
    /// </summary>
    public string Result { get; set; }

    /// <summary>
    /// Gets or sets where the call stands.
    /// </summary>
    public ChatTurnToolCallStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the id of the approval request, when the call waits or waited for an approval.
    /// </summary>
    public string ApprovalRequestId { get; set; }
}
