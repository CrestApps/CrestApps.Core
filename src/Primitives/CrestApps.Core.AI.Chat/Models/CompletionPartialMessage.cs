using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Models;

/// <summary>
/// Represents the completion Partial Message.
/// </summary>
public sealed class CompletionPartialMessage
{
    /// <summary>
    /// Gets or sets the message ID.
    /// </summary>
    public string MessageId { get; set; }

    /// <summary>
    /// Gets or sets the response ID.
    /// </summary>
    public string ResponseId { get; set; }

    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// Gets or sets the session ID.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the references.
    /// </summary>
    public Dictionary<string, AICompletionReference> References { get; set; }

    /// <summary>
    /// Gets or sets the appearance.
    /// </summary>
    public AssistantMessageAppearance Appearance { get; set; }
}
