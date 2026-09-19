using CrestApps.Core.AI.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

/// <summary>
/// YesSql map index for <see cref="AIChatSession"/>, storing key session fields
/// to support efficient filtering by profile, user, status, and activity time.
/// </summary>
public sealed class AIChatSessionIndex : MapIndex
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
    /// Gets or sets the identifier of the user who owns the session.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets the current status of the chat session.
    /// </summary>
    public ChatSessionStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time of the most recent activity in the session.
    /// </summary>
    public DateTime LastActivityUtc { get; set; }
}
