using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Provides shared chat-session analytics operations for recording and querying
/// session lifecycle, performance, conversion, and feedback metrics.
/// </summary>
public interface IAIChatSessionEventService : IAIChatSessionAnalyticsRecorder, IAIChatSessionConversionGoalRecorder
{
    /// <summary>
    /// Records that a chat session has started.
    /// </summary>
    /// <param name="chatSession">The chat session.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RecordSessionStartedAsync(
        AIChatSession chatSession,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the final analytics state for a chat session.
    /// </summary>
    /// <param name="chatSession">The chat session.</param>
    /// <param name="promptCount">The total prompt count.</param>
    /// <param name="isResolved">Whether the session was resolved.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RecordSessionEndedAsync(
        AIChatSession chatSession,
        int promptCount,
        bool isResolved,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records token usage for a chat session.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <param name="inputTokens">The number of input tokens.</param>
    /// <param name="outputTokens">The number of output tokens.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RecordCompletionUsageAsync(
        string sessionId,
        int inputTokens,
        int outputTokens,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records response-latency data for a chat session.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <param name="responseLatencyMs">The response latency in milliseconds.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RecordResponseLatencyAsync(
        string sessionId,
        double responseLatencyMs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records user-rating totals for a chat session.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <param name="thumbsUpCount">The number of positive ratings.</param>
    /// <param name="thumbsDownCount">The number of negative ratings.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RecordUserRatingAsync(
        string sessionId,
        int thumbsUpCount,
        int thumbsDownCount,
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
