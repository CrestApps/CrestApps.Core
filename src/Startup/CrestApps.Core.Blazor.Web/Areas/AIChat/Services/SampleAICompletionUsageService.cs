using System.Collections.Concurrent;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Services;

public sealed class SampleAICompletionUsageService : IAICompletionUsageObserver
{
    private static readonly ConcurrentBag<AICompletionUsageRecord> _store = [];

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly SampleAIChatSessionEventService _chatSessionEventService;
    private readonly GeneralAIOptions _generalAIOptions;

    public SampleAICompletionUsageService(
        IHttpContextAccessor httpContextAccessor,
        SampleAIChatSessionEventService chatSessionEventService,
        IOptions<GeneralAIOptions> generalAIOptions)
    {
        _httpContextAccessor = httpContextAccessor;
        _chatSessionEventService = chatSessionEventService;
        _generalAIOptions = generalAIOptions.Value;
    }

    public async Task UsageRecordedAsync(AICompletionUsageRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!_generalAIOptions.EnableAIUsageTracking)
        {
            return;
        }

        record.CreatedUtc = TimeProvider.System.GetUtcNow().UtcDateTime;

        if (string.IsNullOrEmpty(record.UserName))
        {
            record.UserName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        }

        _store.Add(record);

        if (!string.IsNullOrEmpty(record.SessionId) &&
            (record.InputTokenCount > 0 || record.OutputTokenCount > 0))
        {
            await _chatSessionEventService.RecordCompletionUsageAsync(record.SessionId, record.InputTokenCount, record.OutputTokenCount);
        }
    }

    public Task<IReadOnlyList<AICompletionUsageRecord>> GetAsync(
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default)
    {
        var values = _store.AsEnumerable();

        if (startDateUtc.HasValue)
        {
            var start = startDateUtc.Value.Date;
            values = values.Where(x => x.CreatedUtc >= start);
        }

        if (endDateUtc.HasValue)
        {
            var endExclusive = endDateUtc.Value.Date.AddDays(1);
            values = values.Where(x => x.CreatedUtc < endExclusive);
        }

        IReadOnlyList<AICompletionUsageRecord> result = values
            .OrderByDescending(x => x.CreatedUtc)
            .ToList();

        return Task.FromResult(result);
    }
}
