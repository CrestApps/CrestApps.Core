using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using Microsoft.Extensions.AI;
using YesSql;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.BackgroundServices;

/// <summary>
/// Periodically finalizes inactive AI chat sessions and marks them for post-session processing.
/// Mirrors the behavior of Orchard Core's AIChatSessionCloseBackgroundTask.
/// </summary>
public sealed class AIChatSessionCloseBackgroundService : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _defaultInactivityTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AIChatSessionCloseBackgroundService> _logger;

    public AIChatSessionCloseBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AIChatSessionCloseBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var session = scope.ServiceProvider.GetRequiredService<ISession>();
                var profileManager = scope.ServiceProvider.GetRequiredService<IAIProfileManager>();
                var postCloseProcessor = scope.ServiceProvider.GetRequiredService<AIChatSessionPostCloseProcessor>();
                var promptStore = scope.ServiceProvider.GetRequiredService<IAIChatSessionPromptStore>();
                var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

                await CloseInactiveSessionsAsync(session, profileManager, promptStore, postCloseProcessor, utcNow, stoppingToken);
                await RetryPendingProcessingAsync(session, profileManager, promptStore, postCloseProcessor, utcNow, stoppingToken);

                await session.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while closing inactive AI chat sessions.");
            }
        }
    }

    /// <summary>
    /// Finds active sessions that have exceeded their profile's inactivity timeout and finalizes them.
    /// </summary>
    private async Task CloseInactiveSessionsAsync(
        ISession session,
        IAIProfileManager profileManager,
        IAIChatSessionPromptStore promptStore,
        AIChatSessionPostCloseProcessor postCloseProcessor,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var profiles = await profileManager.GetAsync(AIProfileType.Chat, cancellationToken);

        foreach (var profile in profiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var settings = profile.GetOrCreateSettings<AIProfileDataExtractionSettings>();
            var timeout = settings?.SessionInactivityTimeoutInMinutes > 0
                ? TimeSpan.FromMinutes(settings.SessionInactivityTimeoutInMinutes)
                : _defaultInactivityTimeout;

            var cutoffUtc = utcNow - timeout;

            var inactiveSessions = await session
                .Query<AIChatSession, AIChatSessionIndex>(
                    i => i.ProfileId == profile.ItemId
                        && i.Status == ChatSessionStatus.Active
                            && i.LastActivityUtc < cutoffUtc)
                            .ListAsync(cancellationToken);

            foreach (var chatSession in inactiveSessions)
            {
                var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
                chatSession.Status = DetermineInactiveSessionStatus(prompts);
                chatSession.ClosedAtUtc = utcNow;

                if (AIChatSessionPostCloseProcessor.NeedsProcessing(profile, chatSession))
                {
                    await postCloseProcessor.ProcessAsync(profile, chatSession, prompts, cancellationToken);
                }
                else
                {
                    chatSession.PostSessionProcessingStatus = PostSessionProcessingStatus.None;
                }

                await session.SaveAsync(chatSession);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Finalized inactive session '{SessionId}' for profile '{ProfileId}' as '{Status}'. Post-processing: {NeedsProcessing}.",
                        chatSession.SessionId,
                        profile.ItemId,
                        chatSession.Status,
                        chatSession.PostSessionProcessingStatus != PostSessionProcessingStatus.None);
                }
            }
        }
    }
    private async Task RetryPendingProcessingAsync(
        ISession session,
        IAIProfileManager profileManager,
        IAIChatSessionPromptStore promptStore,
        AIChatSessionPostCloseProcessor postCloseProcessor,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var pendingSessions = await session
            .Query<AIChatSession, AIChatSessionIndex>(
                i => i.Status == ChatSessionStatus.Closed
                    || i.Status == ChatSessionStatus.Abandoned)
                    .ListAsync(cancellationToken);

        foreach (var chatSession in pendingSessions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (chatSession.PostSessionProcessingStatus != PostSessionProcessingStatus.Pending)
            {
                continue;
            }

            if (chatSession.PostSessionProcessingAttempts >= AIChatSessionPostCloseProcessor.MaxPostCloseAttempts)
            {
                chatSession.PostSessionProcessingStatus = PostSessionProcessingStatus.Failed;
                await session.SaveAsync(chatSession);

                _logger.LogWarning(
                    "Post-session processing for session '{SessionId}' failed after {MaxAttempts} attempts.",
                    chatSession.SessionId,
                    AIChatSessionPostCloseProcessor.MaxPostCloseAttempts);
                continue;
            }

            if (chatSession.PostSessionProcessingLastAttemptUtc.HasValue
                && (utcNow - chatSession.PostSessionProcessingLastAttemptUtc.Value) < _retryDelay)
            {
                continue;
            }

            var profile = await profileManager.FindByIdAsync(chatSession.ProfileId, cancellationToken);

            if (profile == null)
            {
                continue;
            }

            var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
            await postCloseProcessor.ProcessAsync(profile, chatSession, prompts, cancellationToken);
            await session.SaveAsync(chatSession);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Processed pending post-close work for session '{SessionId}'.",
                    chatSession.SessionId);
            }
        }
    }

    private static ChatSessionStatus DetermineInactiveSessionStatus(IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);

        return prompts.Any(prompt => prompt.Role == ChatRole.User)
                    ? ChatSessionStatus.Closed
                    : ChatSessionStatus.Abandoned;
    }
}
