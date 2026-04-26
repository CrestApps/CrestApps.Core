using System.Collections.Concurrent;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Services;

public sealed class SampleAIChatSessionEventService
{
    private static readonly ConcurrentDictionary<string, AIChatSessionEvent> _store = new(StringComparer.OrdinalIgnoreCase);

    public Task RecordSessionStartedAsync(AIChatSession chatSession)
    {
        ArgumentNullException.ThrowIfNull(chatSession);

        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var isAuthenticated = !string.IsNullOrEmpty(chatSession.UserId);
        var evt = new AIChatSessionEvent
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
        _store[chatSession.SessionId] = evt;

return Task.CompletedTask;
    }

    public Task RecordSessionEndedAsync(AIChatSession chatSession, int promptCount, bool isResolved)
    {
        ArgumentNullException.ThrowIfNull(chatSession);

        if (!_store.TryGetValue(chatSession.SessionId, out var evt))
        {
            var now = TimeProvider.System.GetUtcNow().UtcDateTime;
            var isAuthenticated = !string.IsNullOrEmpty(chatSession.UserId);
            evt = new AIChatSessionEvent
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
            _store[chatSession.SessionId] = evt;

return Task.CompletedTask;
        }

        var endTime = chatSession.ClosedAtUtc ?? TimeProvider.System.GetUtcNow().UtcDateTime;
        evt.SessionEndedUtc = endTime;
        evt.MessageCount = promptCount;
        evt.IsResolved = isResolved;
        evt.HandleTimeSeconds = (endTime - evt.SessionStartedUtc).TotalSeconds;

return Task.CompletedTask;
    }

    public Task RecordCompletionUsageAsync(string sessionId, int inputTokens, int outputTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_store.TryGetValue(sessionId, out var evt))
        {
            return Task.CompletedTask;
        }

        evt.TotalInputTokens += inputTokens;
        evt.TotalOutputTokens += outputTokens;

return Task.CompletedTask;
    }

    public Task RecordResponseLatencyAsync(string sessionId, double responseLatencyMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_store.TryGetValue(sessionId, out var evt) || responseLatencyMs <= 0)
        {
            return Task.CompletedTask;
        }

        evt.CompletionCount++;
        evt.AverageResponseLatencyMs = ((evt.AverageResponseLatencyMs * (evt.CompletionCount - 1)) + responseLatencyMs) / evt.CompletionCount;

return Task.CompletedTask;
    }

    public Task RecordConversionMetricsAsync(string sessionId, List<ConversionGoalResult> goalResults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(goalResults);

        if (!_store.TryGetValue(sessionId, out var evt))
        {
            return Task.CompletedTask;
        }

        evt.ConversionGoalResults = goalResults;
        evt.ConversionScore = goalResults.Sum(result => result.Score);
        evt.ConversionMaxScore = goalResults.Sum(result => result.MaxScore);

return Task.CompletedTask;
    }

    public Task RecordUserRatingAsync(string sessionId, int thumbsUpCount, int thumbsDownCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_store.TryGetValue(sessionId, out var evt))
        {
            return Task.CompletedTask;
        }

        evt.ThumbsUpCount = thumbsUpCount;
        evt.ThumbsDownCount = thumbsDownCount;
        evt.UserRating = thumbsUpCount + thumbsDownCount > 0 ? thumbsUpCount >= thumbsDownCount : null;

return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AIChatSessionEvent>> GetAsync(string profileId, DateTime? startDateUtc, DateTime? endDateUtc, CancellationToken cancellationToken = default)
    {
        var values = _store.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(profileId))
        {
            values = values.Where(x => string.Equals(x.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        }

        if (startDateUtc.HasValue)
        {
            var start = startDateUtc.Value.Date;
            values = values.Where(x => x.SessionStartedUtc >= start);
        }

        if (endDateUtc.HasValue)
        {
            var endExclusive = endDateUtc.Value.Date.AddDays(1);
            values = values.Where(x => x.SessionStartedUtc < endExclusive);
        }

        IReadOnlyList<AIChatSessionEvent> result = values
            .OrderByDescending(x => x.SessionStartedUtc)
            .ToList();

return Task.FromResult(result);
    }
}
