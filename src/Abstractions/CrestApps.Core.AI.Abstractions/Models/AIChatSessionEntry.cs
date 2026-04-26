namespace CrestApps.Core.AI.Models;

/// <summary>
/// A lightweight representation of an AI chat session used for listing purposes.
/// Contains only the fields needed to display session summaries without loading the full document.
/// </summary>
public sealed class AIChatSessionEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for the chat session.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the AI profile identifier associated with this session.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the display title for this session.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who owns this session.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets the client identifier used when <see cref="UserId"/> is unavailable.
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the current lifecycle status of this session.
    /// </summary>
    public ChatSessionStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this session was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the most recent activity in this session.
    /// </summary>
    public DateTime LastActivityUtc { get; set; }
}
