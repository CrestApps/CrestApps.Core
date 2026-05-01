using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Records session analytics data during message processing when analytics are enabled
/// for the active AI profile.
/// </summary>
public sealed class DefaultAIChatSessionAnalyticsHandler : AIChatSessionHandlerBase
{
    private readonly IAIChatSessionEventService _eventService;
    private readonly ILogger<DefaultAIChatSessionAnalyticsHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIChatSessionAnalyticsHandler"/> class.
    /// </summary>
    /// <param name="eventService">The chat-session analytics service.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAIChatSessionAnalyticsHandler(
        IAIChatSessionEventService eventService,
        ILogger<DefaultAIChatSessionAnalyticsHandler> logger)
    {
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Records analytics data after a chat message has completed.
    /// </summary>
    /// <param name="context">The completed-message context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task MessageCompletedAsync(
        ChatMessageCompletedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Profile.TryGet<AnalyticsMetadata>(out var analyticsMetadata) || !analyticsMetadata.EnableSessionMetrics)
        {
            return;
        }

        try
        {
            var userMessageCount = context.Prompts.Count(prompt => prompt.Role == ChatRole.User);

            if (userMessageCount == 1)
            {
                await _eventService.RecordSessionStartedAsync(context.ChatSession, cancellationToken);
            }

            if (context.ResponseLatencyMs > 0)
            {
                await _eventService.RecordResponseLatencyAsync(context.ChatSession.SessionId, context.ResponseLatencyMs, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record analytics event for session '{SessionId}'.", context.ChatSession.SessionId);
        }
    }
}
