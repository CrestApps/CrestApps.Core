using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Handles the "end-session" notification action.
/// Closes the chat session and sends a "session ended" notification to the UI.
/// </summary>
public sealed class EndSessionNotificationActionHandler : IChatNotificationActionHandler
{
    /// <summary>
    /// Handles the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task HandleAsync(ChatNotificationActionContext context, CancellationToken cancellationToken)
    {
        var logger = context.Services.GetRequiredService<ILogger<EndSessionNotificationActionHandler>>();
        var notificationSender = context.Services.GetRequiredService<IChatNotificationSender>();
        var T = context.Services.GetRequiredService<IStringLocalizer<EndSessionNotificationActionHandler>>();

        if (context.ChatType == ChatContextType.AIChatSession)
        {
            var profileManager = context.Services.GetRequiredService<IAIProfileManager>();
            var sessionManager = context.Services.GetRequiredService<IAIChatSessionManager>();
            var postCloseProcessor = context.Services.GetRequiredService<AIChatSessionPostCloseProcessor>();
            var session = await sessionManager.FindByIdAsync(context.SessionId, cancellationToken);

            if (session is null)
            {
                logger.LogWarning("End session failed: session '{SessionId}' not found.", context.SessionId);

                return;
            }

            var timeProvider = context.Services.GetService<TimeProvider>() ?? TimeProvider.System;

            session.Status = ChatSessionStatus.Closed;
            session.ClosedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            var queuedPostCloseProcessing = false;
            var profile = await profileManager.FindByIdAsync(session.ProfileId, cancellationToken);

            if (profile is null)
            {
                logger.LogWarning(
                    "End session for '{SessionId}' closed the session, but profile '{ProfileId}' was not found so post-close work could not be queued.",
                    context.SessionId,
                    session.ProfileId);
            }
            else
            {
                queuedPostCloseProcessing = postCloseProcessor.QueueIfNeeded(profile, session);
            }

            await sessionManager.SaveAsync(session, cancellationToken);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Session '{SessionId}' ended via notification action. Queued post-close processing: {QueuedPostCloseProcessing}.",
                    context.SessionId,
                    queuedPostCloseProcessing);
            }
        }

        // Show a "session ended" notification.
        await notificationSender.SendAsync(
            context.SessionId,
            context.ChatType,
            new ChatNotification(ChatNotificationTypes.SessionEnded)
            {
                Content = T["This chat session has ended."].Value,
                Icon = "fa-solid fa-circle-check",
                Dismissible = true,
            });
    }
}
