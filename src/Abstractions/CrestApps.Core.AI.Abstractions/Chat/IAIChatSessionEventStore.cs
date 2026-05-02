using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Persists chat-session analytics records for reporting and post-session analysis.
/// </summary>
public interface IAIChatSessionEventStore
{
    /// <summary>
    /// Finds a chat-session analytics record by session identifier.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching analytics record, or <see langword="null"/> if not found.</returns>
    Task<AIChatSessionEvent> FindBySessionIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a chat-session analytics record.
    /// </summary>
    /// <param name="chatSessionEvent">The analytics record to save.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SaveAsync(
        AIChatSessionEvent chatSessionEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves chat-session analytics records matching the optional profile and date filters.
    /// </summary>
    /// <param name="profileId">The optional profile identifier filter.</param>
    /// <param name="startDateUtc">The inclusive UTC start date filter.</param>
    /// <param name="endDateUtc">The inclusive UTC end date filter.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching chat-session events ordered by session start descending.</returns>
    Task<IReadOnlyList<AIChatSessionEvent>> GetAsync(
        string profileId,
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default);
}
