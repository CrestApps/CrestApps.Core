using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Provides unscoped persistence access to AI chat sessions.
/// Unlike <see cref="IAIChatSessionManager"/> which may apply user-scoping or
/// business rules, this store offers direct data access suitable for background
/// processing, administrative tasks, and system-level operations.
/// </summary>
public interface IAIChatSessionStore
{
    /// <summary>
    /// Asynchronously retrieves a chat session by its unique session identifier
    /// without applying user-scoping or ownership checks.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the chat session.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The matching session, or <see langword="null"/> if not found.</returns>
    Task<AIChatSession> FindByIdAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves all active sessions for the specified profile that have
    /// been inactive since before the given cutoff time.
    /// </summary>
    /// <param name="profileId">The profile identifier to filter sessions by.</param>
    /// <param name="cutoffUtc">The UTC cutoff time; sessions with last activity before this are returned.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of inactive active sessions.</returns>
    Task<IReadOnlyList<AIChatSession>> GetInactiveActiveSessionsAsync(
        string profileId,
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves all closed or abandoned sessions for the specified profile
    /// that may require post-close processing.
    /// </summary>
    /// <param name="profileId">The profile identifier to filter sessions by.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of closed or abandoned sessions.</returns>
    Task<IReadOnlyList<AIChatSession>> GetClosedSessionsAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves a profile's sessions by their last activity, oldest first, without applying
    /// user-scoping. Used to walk a profile's conversations in batches, for example to apply its storage mode or
    /// retention.
    /// </summary>
    /// <param name="profileId">The profile identifier to filter sessions by.</param>
    /// <param name="afterUtc">When set, only sessions whose last activity is later than this are returned.</param>
    /// <param name="beforeUtc">Only sessions whose last activity is earlier than this are returned.</param>
    /// <param name="count">The largest number of sessions to return.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The sessions, ordered by their last activity.</returns>
    Task<IReadOnlyList<AIChatSession>> ListByLastActivityAsync(
        string profileId,
        DateTime? afterUtc,
        DateTime beforeUtc,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously persists the specified chat session.
    /// </summary>
    /// <param name="chatSession">The chat session to save.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task SaveAsync(AIChatSession chatSession, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously deletes a chat session and its prompts without applying user-scoping. Unlike
    /// <see cref="IAIChatSessionManager.DeleteAsync(string, CancellationToken)"/>, this deletes a session of any
    /// user, visitor or system conversation, so it is meant for retention, privacy and administrative tasks. The
    /// session's documents are left to the caller.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the chat session.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when the session existed and was deleted; otherwise <see langword="false"/>.</returns>
    Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}
