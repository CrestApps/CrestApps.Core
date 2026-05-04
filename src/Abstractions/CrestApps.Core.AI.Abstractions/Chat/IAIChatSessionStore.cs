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
    /// Asynchronously persists the specified chat session.
    /// </summary>
    /// <param name="chatSession">The chat session to save.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task SaveAsync(AIChatSession chatSession, CancellationToken cancellationToken = default);
}
