namespace CrestApps.Core.Data.EntityCore.Models;

/// <summary>
/// Represents the Entity Framework Core database record for a chat-session analytics event.
/// </summary>
public sealed class AIChatSessionEventStoreRecord
{
    /// <summary>
    /// Gets or sets the unique identifier of the chat session.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the AI profile associated with the session.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the session started.
    /// </summary>
    public DateTime SessionStartedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the analytics record was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the serialized JSON payload containing the full analytics event.
    /// </summary>
    public string Payload { get; set; }
}
