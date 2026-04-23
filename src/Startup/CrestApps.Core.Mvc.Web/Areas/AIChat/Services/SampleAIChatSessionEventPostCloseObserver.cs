using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.Services;

public sealed class SampleAIChatSessionEventPostCloseObserver : IAIChatSessionAnalyticsRecorder, IAIChatSessionConversionGoalRecorder
{
    private readonly SampleAIChatSessionEventService _eventService;

    public SampleAIChatSessionEventPostCloseObserver(SampleAIChatSessionEventService eventService)
    {
        _eventService = eventService;
    }

    public async Task RecordSessionEndedAsync(AIProfile profile, AIChatSession session, IReadOnlyList<AIChatSessionPrompt> prompts, bool isResolved, CancellationToken cancellationToken = default)
    {
        await _eventService.RecordSessionEndedAsync(session, prompts.Count, isResolved);
    }

    public async Task RecordConversionGoalsAsync(AIProfile profile, AIChatSession session, IReadOnlyList<ConversionGoalResult> goalResults, CancellationToken cancellationToken = default)
    {
        await _eventService.RecordConversionMetricsAsync(session.SessionId, goalResults.ToList());
    }
}
