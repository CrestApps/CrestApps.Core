using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.BackgroundServices;

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
                var sessionManager = scope.ServiceProvider.GetRequiredService<IAIChatSessionManager>();
                var profileManager = scope.ServiceProvider.GetRequiredService<IAIProfileManager>();
                var postCloseProcessor = scope.ServiceProvider.GetRequiredService<AIChatSessionPostCloseProcessor>();
                var promptStore = scope.ServiceProvider.GetRequiredService<IAIChatSessionPromptStore>();
                var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

                await CloseInactiveSessionsAsync(sessionManager, profileManager, promptStore, postCloseProcessor, utcNow, stoppingToken);
                await RetryPendingProcessingAsync(sessionManager, profileManager, promptStore, postCloseProcessor, utcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "An error occurred while closing inactive AI chat sessions."); }
        }
    }

    private async Task CloseInactiveSessionsAsync(
        IAIChatSessionManager sessionManager,
        IAIProfileManager profileManager,
        IAIChatSessionPromptStore promptStore,
        AIChatSessionPostCloseProcessor postCloseProcessor,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var profiles = await profileManager.GetAsync(AIProfileType.Chat);
        foreach (var profile in profiles)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var settings = profile.GetSettings<AIProfileDataExtractionSettings>();
            var timeout = settings?.SessionInactivityTimeoutInMinutes > 0
                ? TimeSpan.FromMinutes(settings.SessionInactivityTimeoutInMinutes) : _defaultInactivityTimeout;
            var cutoffUtc = utcNow - timeout;

            var context = new AIChatSessionQueryContext { ProfileId = profile.ItemId };
            var result = await sessionManager.PageAsync(1, 100, context);
            var inactiveEntries = result.Sessions
                .Where(s => s.Status == ChatSessionStatus.Active && s.LastActivityUtc < cutoffUtc);

            foreach (var entry in inactiveEntries)
            {
                var chatSession = await sessionManager.FindByIdAsync(entry.SessionId);
                if (chatSession is null || chatSession.Status != ChatSessionStatus.Active) continue;

                chatSession.Status = ChatSessionStatus.Closed;
                chatSession.ClosedAtUtc = utcNow;
                if (AIChatSessionPostCloseProcessor.NeedsProcessing(profile, chatSession))
                {
                    var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
                    await postCloseProcessor.ProcessAsync(profile, chatSession, prompts, cancellationToken);
                }
                else
                {
                    chatSession.PostSessionProcessingStatus = PostSessionProcessingStatus.None;
                }
                await sessionManager.SaveAsync(chatSession);
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Closed inactive session '{SessionId}' for profile '{ProfileId}'.", chatSession.SessionId, profile.ItemId);
            }
        }
    }

    private async Task RetryPendingProcessingAsync(
        IAIChatSessionManager sessionManager,
        IAIProfileManager profileManager,
        IAIChatSessionPromptStore promptStore,
        AIChatSessionPostCloseProcessor postCloseProcessor,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var profiles = await profileManager.GetAsync(AIProfileType.Chat);
        foreach (var profile in profiles)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var context = new AIChatSessionQueryContext { ProfileId = profile.ItemId };
            var result = await sessionManager.PageAsync(1, 100, context);
            var closedEntries = result.Sessions
                .Where(s => s.Status == ChatSessionStatus.Closed);

            foreach (var entry in closedEntries)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var chatSession = await sessionManager.FindByIdAsync(entry.SessionId);
                if (chatSession is null || chatSession.PostSessionProcessingStatus != PostSessionProcessingStatus.Pending) continue;

                if (chatSession.PostSessionProcessingAttempts >= AIChatSessionPostCloseProcessor.MaxPostCloseAttempts)
                {
                    chatSession.PostSessionProcessingStatus = PostSessionProcessingStatus.Failed;
                    await sessionManager.SaveAsync(chatSession);
                    _logger.LogWarning("Post-session processing for session '{SessionId}' failed after {MaxAttempts} attempts.", chatSession.SessionId, AIChatSessionPostCloseProcessor.MaxPostCloseAttempts);
                    continue;
                }
                if (chatSession.PostSessionProcessingLastAttemptUtc.HasValue && (utcNow - chatSession.PostSessionProcessingLastAttemptUtc.Value) < _retryDelay) continue;
                var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
                await postCloseProcessor.ProcessAsync(profile, chatSession, prompts, cancellationToken);
                await sessionManager.SaveAsync(chatSession);
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Processed pending post-close work for session '{SessionId}'.", chatSession.SessionId);
            }
        }
    }
}
