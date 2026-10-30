namespace CrestApps.Core.AI.Models;

/// <summary>
/// The running memory of one conversation, stored on its <see cref="AIChatSession"/> or
/// <see cref="ChatInteraction"/>. When a conversation grows past the profile's past-messages count, the
/// messages that fall out of that window are folded into <see cref="Summary"/>, so the assistant keeps what
/// was said early in a long conversation without being sent the whole transcript.
/// </summary>
public sealed class AIConversationMemory
{
    /// <summary>
    /// Gets or sets the summary of the oldest <see cref="SummarizedMessageCount"/> messages of the conversation.
    /// </summary>
    public string Summary { get; set; }

    /// <summary>
    /// Gets or sets how many messages, counted from the start of the conversation's user and assistant messages,
    /// <see cref="Summary"/> covers.
    /// </summary>
    public int SummarizedMessageCount { get; set; }

    /// <summary>
    /// Gets or sets when <see cref="Summary"/> was last updated.
    /// </summary>
    public DateTime? UpdatedUtc { get; set; }
}
