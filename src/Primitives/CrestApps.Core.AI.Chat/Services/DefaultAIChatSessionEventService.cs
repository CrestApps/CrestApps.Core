using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Default framework service for recording and querying chat-session analytics.
/// </summary>
public sealed class DefaultAIChatSessionEventService : IAIChatSessionEventService
{
    private readonly IAIChatSessionEventStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIChatSessionEventService"/> class.
    /// </summary>
    /// <param name="store">The analytics store.</param>
    /// <param name="timeProvider">The time provider.</param>
    public DefaultAIChatSessionEventService(
        IAIChatSessionEventStore store,
        TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Records that a chat session has started.
    /// </summary>
    /// <param name="chatSession">The chat session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RecordSessionStartedAsync(
        AIChatSession chatSession,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatSession);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var isAuthenticated = !string.IsNullOrEmpty(chatSession.UserId);
        var chatSessionEvent = new AIChatSessionEvent
        {
            SessionId = chatSession.SessionId,
            ProfileId = chatSession.ProfileId,
            VisitorId = isAuthenticated ? chatSession.UserId : chatSession.ClientId ?? string.Empty,
            UserId = chatSession.UserId,
            IsAuthenticated = isAuthenticated,
            SessionStartedUtc = now,
            MessageCount = 0,
            HandleTimeSeconds = 0,
            IsResolved = false,
            CompletionCount = 0,
            CreatedUtc = now,
        };

        await _store.SaveAsync(chatSessionEvent, cancellationToken);
    }

    /// <summary>
    /// Records the final analytics state for a chat session.
    /// </summary>
    /// <param name="chatSession">The chat session.</param>
    /// <param name="promptCount">The total prompt count.</param>
    /// <param name="isResolved">Whether the session was resolved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RecordSessionEndedAsync(
        AIChatSession chatSession,
        int promptCount,
        bool isResolved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatSession);

        var chatSessionEvent = await _store.FindBySessionIdAsync(chatSession.SessionId, cancellationToken);
        if (chatSessionEvent is null)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var isAuthenticated = !string.IsNullOrEmpty(chatSession.UserId);
            chatSessionEvent = new AIChatSessionEvent
            {
                SessionId = chatSession.SessionId,
                ProfileId = chatSession.ProfileId,
                VisitorId = isAuthenticated ? chatSession.UserId : chatSession.ClientId ?? string.Empty,
                UserId = chatSession.UserId,
                IsAuthenticated = isAuthenticated,
                SessionStartedUtc = chatSession.CreatedUtc,
                SessionEndedUtc = chatSession.ClosedAtUtc ?? now,
                MessageCount = promptCount,
                HandleTimeSeconds = ((chatSession.ClosedAtUtc ?? now) - chatSession.CreatedUtc).TotalSeconds,
                IsResolved = isResolved,
                CompletionCount = 0,
                CreatedUtc = now,
            };

            await _store.SaveAsync(chatSessionEvent, cancellationToken);

            return;
        }

        var endTime = chatSession.ClosedAtUtc ?? _timeProvider.GetUtcNow().UtcDateTime;
        chatSessionEvent.SessionEndedUtc = endTime;
        chatSessionEvent.MessageCount = promptCount;
        chatSessionEvent.IsResolved = isResolved;
        chatSessionEvent.HandleTimeSeconds = (endTime - chatSessionEvent.SessionStartedUtc).TotalSeconds;

        await _store.SaveAsync(chatSessionEvent, cancellationToken);
    }

    /// <summary>
    /// Records completion-usage totals for a chat session.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <param name="inputTokens">The number of input tokens.</param>
    /// <param name="outputTokens">The number of output tokens.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RecordCompletionUsageAsync(
        string sessionId,
        int inputTokens,
        int outputTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var chatSessionEvent = await _store.FindBySessionIdAsync(sessionId, cancellationToken);
        if (chatSessionEvent is null)
        {
            return;
        }

        chatSessionEvent.TotalInputTokens += inputTokens;
        chatSessionEvent.TotalOutputTokens += outputTokens;

        await _store.SaveAsync(chatSessionEvent, cancellationToken);
    }

    /// <summary>
    /// Records response-latency data for a chat session.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <param name="responseLatencyMs">The response latency in milliseconds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RecordResponseLatencyAsync(
        string sessionId,
        double responseLatencyMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var chatSessionEvent = await _store.FindBySessionIdAsync(sessionId, cancellationToken);
        if (chatSessionEvent is null || responseLatencyMs <= 0)
        {
            return;
        }

        chatSessionEvent.CompletionCount++;
        chatSessionEvent.AverageResponseLatencyMs =
            ((chatSessionEvent.AverageResponseLatencyMs * (chatSessionEvent.CompletionCount - 1)) + responseLatencyMs)
            / chatSessionEvent.CompletionCount;

        await _store.SaveAsync(chatSessionEvent, cancellationToken);
    }

    /// <summary>
    /// Records user-rating totals for a chat session.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <param name="thumbsUpCount">The number of positive ratings.</param>
    /// <param name="thumbsDownCount">The number of negative ratings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RecordUserRatingAsync(
        string sessionId,
        int thumbsUpCount,
        int thumbsDownCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var chatSessionEvent = await _store.FindBySessionIdAsync(sessionId, cancellationToken);
        if (chatSessionEvent is null)
        {
            return;
        }

        chatSessionEvent.ThumbsUpCount = thumbsUpCount;
        chatSessionEvent.ThumbsDownCount = thumbsDownCount;
        chatSessionEvent.UserRating = thumbsUpCount + thumbsDownCount > 0 ? thumbsUpCount >= thumbsDownCount : null;

        await _store.SaveAsync(chatSessionEvent, cancellationToken);
    }

    /// <summary>
    /// Retrieves chat-session analytics records matching the optional profile and date filters.
    /// </summary>
    /// <param name="profileId">The optional profile identifier filter.</param>
    /// <param name="startDateUtc">The inclusive UTC start date filter.</param>
    /// <param name="endDateUtc">The inclusive UTC end date filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<IReadOnlyList<AIChatSessionEvent>> GetAsync(
        string profileId,
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(profileId, startDateUtc, endDateUtc, cancellationToken);
    }

    /// <summary>
    /// Records end-of-session analytics for the specified chat session.
    /// </summary>
    /// <param name="profile">The AI profile associated with the session.</param>
    /// <param name="session">The chat session.</param>
    /// <param name="prompts">The prompts exchanged during the session.</param>
    /// <param name="isResolved">Whether the session was resolved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RecordSessionEndedAsync(
        AIProfile profile,
        AIChatSession session,
        IReadOnlyList<AIChatSessionPrompt> prompts,
        bool isResolved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(prompts);

        return RecordSessionEndedAsync(session, prompts.Count, isResolved, cancellationToken);
    }

    /// <summary>
    /// Records evaluated conversion-goal results for the specified chat session.
    /// </summary>
    /// <param name="profile">The AI profile associated with the session.</param>
    /// <param name="session">The chat session.</param>
    /// <param name="goalResults">The evaluated conversion-goal results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RecordConversionGoalsAsync(
        AIProfile profile,
        AIChatSession session,
        IReadOnlyList<ConversionGoalResult> goalResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(goalResults);

        var chatSessionEvent = await _store.FindBySessionIdAsync(session.SessionId, cancellationToken);
        if (chatSessionEvent is null)
        {
            return;
        }

        chatSessionEvent.ConversionGoalResults = goalResults.ToList();
        chatSessionEvent.ConversionScore = goalResults.Sum(result => result.Score);
        chatSessionEvent.ConversionMaxScore = goalResults.Sum(result => result.MaxScore);

        await _store.SaveAsync(chatSessionEvent, cancellationToken);
    }
}
