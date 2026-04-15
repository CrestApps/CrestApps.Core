using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Services;

public sealed class MvcAIChatSessionEventService
{
    private readonly BlazorAppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public MvcAIChatSessionEventService(BlazorAppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task RecordSessionStartedAsync(AIChatSession chatSession)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
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
        _dbContext.SessionEvents.Add(evt);
        await _dbContext.SaveChangesAsync();
    }

    public async Task RecordSessionEndedAsync(AIChatSession chatSession, int promptCount, bool isResolved)
    {
        var evt = await FindEventBySessionIdAsync(chatSession.SessionId);
        if (evt is null)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
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
            _dbContext.SessionEvents.Add(evt);
            await _dbContext.SaveChangesAsync();
            return;
        }

        var endTime = chatSession.ClosedAtUtc ?? _timeProvider.GetUtcNow().UtcDateTime;
        evt.SessionEndedUtc = endTime;
        evt.MessageCount = promptCount;
        evt.IsResolved = isResolved;
        evt.HandleTimeSeconds = (endTime - evt.SessionStartedUtc).TotalSeconds;
        await _dbContext.SaveChangesAsync();
    }

    public async Task RecordCompletionUsageAsync(string sessionId, int inputTokens, int outputTokens)
    {
        var evt = await FindEventBySessionIdAsync(sessionId);
        if (evt is null) return;
        evt.TotalInputTokens += inputTokens;
        evt.TotalOutputTokens += outputTokens;
        await _dbContext.SaveChangesAsync();
    }

    public async Task RecordResponseLatencyAsync(string sessionId, double responseLatencyMs)
    {
        var evt = await FindEventBySessionIdAsync(sessionId);
        if (evt is null || responseLatencyMs <= 0) return;
        evt.CompletionCount++;
        evt.AverageResponseLatencyMs = ((evt.AverageResponseLatencyMs * (evt.CompletionCount - 1)) + responseLatencyMs) / evt.CompletionCount;
        await _dbContext.SaveChangesAsync();
    }

    public async Task RecordConversionMetricsAsync(string sessionId, List<ConversionGoalResult> goalResults)
    {
        var evt = await FindEventBySessionIdAsync(sessionId);
        if (evt is null) return;
        evt.ConversionGoalResults = goalResults;
        evt.ConversionScore = goalResults.Sum(r => r.Score);
        evt.ConversionMaxScore = goalResults.Sum(r => r.MaxScore);
        await _dbContext.SaveChangesAsync();
    }

    public async Task RecordUserRatingAsync(string sessionId, int thumbsUpCount, int thumbsDownCount)
    {
        var evt = await FindEventBySessionIdAsync(sessionId);
        if (evt is null) return;
        evt.ThumbsUpCount = thumbsUpCount;
        evt.ThumbsDownCount = thumbsDownCount;
        evt.UserRating = thumbsUpCount + thumbsDownCount > 0 ? thumbsUpCount >= thumbsDownCount : null;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<AIChatSessionEvent>> GetAsync(string profileId, DateTime? startDateUtc, DateTime? endDateUtc, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SessionEvents.AsQueryable();
        if (!string.IsNullOrEmpty(profileId))
            query = query.Where(x => x.ProfileId == profileId);
        if (startDateUtc.HasValue)
            query = query.Where(x => x.SessionStartedUtc >= startDateUtc.Value.Date);
        if (endDateUtc.HasValue)
            query = query.Where(x => x.SessionStartedUtc < endDateUtc.Value.Date.AddDays(1));
        return await query.OrderByDescending(x => x.SessionStartedUtc).ToListAsync(cancellationToken);
    }

    private async Task<AIChatSessionEvent?> FindEventBySessionIdAsync(string sessionId)
        => await _dbContext.SessionEvents.FirstOrDefaultAsync(x => x.SessionId == sessionId);
}
