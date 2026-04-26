using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.EntityCore.Models;

/// <summary>
/// Represents the Entity Framework Core database record for an AI chat session.
/// </summary>
public sealed class AIChatSessionRecord
{
    /// <summary>
    /// Gets or sets the unique identifier of the chat session.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the AI profile associated with this session.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the human-readable title of the chat session.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who owns this chat session.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets the client identifier from which the session was initiated.
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the current status of the chat session.
    /// </summary>
    public ChatSessionStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the session was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time of the most recent activity in the session.
    /// </summary>
    public DateTime LastActivityUtc { get; set; }

    /// <summary>
    /// Gets or sets the serialized JSON payload containing the full session data.
    /// </summary>
    public string Payload { get; set; }
}
