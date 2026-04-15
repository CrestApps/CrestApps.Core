using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Services;

public sealed class MvcAICompletionUsageService : IAICompletionUsageObserver
{
    private readonly BlazorAppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly MvcAIChatSessionEventService _chatSessionEventService;
    private readonly GeneralAIOptions _generalAIOptions;

    public MvcAICompletionUsageService(
        BlazorAppDbContext dbContext,
        TimeProvider timeProvider,
        IHttpContextAccessor httpContextAccessor,
        MvcAIChatSessionEventService chatSessionEventService,
        IOptions<GeneralAIOptions> generalAIOptions)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _httpContextAccessor = httpContextAccessor;
        _chatSessionEventService = chatSessionEventService;
        _generalAIOptions = generalAIOptions.Value;
    }

    public async Task UsageRecordedAsync(AICompletionUsageRecord record, CancellationToken cancellationToken = default)
    {
        if (!_generalAIOptions.EnableAIUsageTracking) return;
        record.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (string.IsNullOrEmpty(record.UserName))
            record.UserName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        _dbContext.UsageRecords.Add(record);
        _dbContext.Entry(record).Property("Id").CurrentValue = Guid.NewGuid().ToString("N");
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrEmpty(record.SessionId) && (record.InputTokenCount > 0 || record.OutputTokenCount > 0))
            await _chatSessionEventService.RecordCompletionUsageAsync(record.SessionId, record.InputTokenCount, record.OutputTokenCount);
    }

    public async Task<IReadOnlyList<AICompletionUsageRecord>> GetAsync(DateTime? startDateUtc, DateTime? endDateUtc, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.UsageRecords.AsQueryable();
        if (startDateUtc.HasValue) query = query.Where(x => x.CreatedUtc >= startDateUtc.Value.Date);
        if (endDateUtc.HasValue) query = query.Where(x => x.CreatedUtc < endDateUtc.Value.Date.AddDays(1));
        return await query.OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken);
    }
}
