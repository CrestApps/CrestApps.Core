using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default framework service for recording and querying AI completion usage.
/// </summary>
public sealed class DefaultAICompletionUsageService : IAICompletionUsageService
{
    private readonly IAICompletionUsageStore _store;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly GeneralAIOptions _generalAIOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAICompletionUsageService"/> class.
    /// </summary>
    /// <param name="store">The usage store.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="generalAIOptions">The general AI options.</param>
    public DefaultAICompletionUsageService(
        IAICompletionUsageStore store,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        IHttpContextAccessor httpContextAccessor,
        IOptions<GeneralAIOptions> generalAIOptions)
    {
        _store = store;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _httpContextAccessor = httpContextAccessor;
        _generalAIOptions = generalAIOptions.Value;
    }

    /// <summary>
    /// Records a completion usage record.
    /// </summary>
    /// <param name="record">The usage record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task UsageRecordedAsync(
        AICompletionUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!_generalAIOptions.EnableAIUsageTracking)
        {
            return;
        }

        record.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (string.IsNullOrEmpty(record.UserName))
        {
            record.UserName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        }

        await _store.SaveAsync(record, cancellationToken);

        if (!string.IsNullOrEmpty(record.SessionId) &&
            (record.InputTokenCount > 0 || record.OutputTokenCount > 0))
        {
            var chatSessionEventService = _serviceProvider.GetService<IAIChatSessionEventService>();

            if (chatSessionEventService is not null)
            {
                await chatSessionEventService.RecordCompletionUsageAsync(
                    record.SessionId,
                    record.InputTokenCount,
                    record.OutputTokenCount,
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Retrieves usage records for the optional UTC date range.
    /// </summary>
    /// <param name="startDateUtc">The inclusive UTC start date filter.</param>
    /// <param name="endDateUtc">The inclusive UTC end date filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<IReadOnlyList<AICompletionUsageRecord>> GetAsync(
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(startDateUtc, endDateUtc, cancellationToken);
    }
}
