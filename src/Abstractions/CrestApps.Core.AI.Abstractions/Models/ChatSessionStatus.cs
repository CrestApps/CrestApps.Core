namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the lifecycle status of an AI chat session.
/// </summary>
public enum ChatSessionStatus
{
    /// <summary>
    /// The session is open and accepting new messages.
    /// </summary>
    Active,

    /// <summary>
    /// The session has been explicitly closed by the user or system.
    /// </summary>
    Closed,

    /// <summary>
    /// The session was left without a proper close (e.g., timed out or abandoned by the user).
    /// </summary>
    Abandoned,
}
