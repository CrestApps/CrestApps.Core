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

    /// <summary>
    /// Gets or sets the name of the agent that made the call, when the call is one an agent the assistant
    /// delegated to made, and that waits or waited for an approval. It is <see langword="null"/> for a call the
    /// assistant made itself.
    /// </summary>
    public string AgentName { get; set; }

    /// <summary>
    /// Gets or sets the display title of the agent named by <see cref="AgentName"/>, which the approval request shows
    /// to the person. <see langword="null"/> when the agent has none, or for a call the assistant made itself.
    /// </summary>
    public string AgentDisplayText { get; set; }

    /// <summary>
    /// Gets or sets the id of the assistant's call to the agent named by <see cref="AgentName"/>.
    /// </summary>
    public string AgentCallId { get; set; }
}
