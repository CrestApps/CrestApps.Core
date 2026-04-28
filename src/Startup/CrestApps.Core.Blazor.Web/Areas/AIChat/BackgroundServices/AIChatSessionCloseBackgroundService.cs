using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.BackgroundServices;

/// <summary>
/// Periodically closes inactive AI chat sessions and marks them for post-session processing.
/// Mirrors the behavior of Orchard Core's AIChatSessionCloseBackgroundTask.
/// </summary>
public sealed class AIChatSessionCloseBackgroundService : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _defaultInactivityTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMinutes(5);
    private const int _pageSize = 100;

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
                var storeCommitter = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();
                var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

                await CloseInactiveSessionsAsync(sessionManager, profileManager, promptStore, postCloseProcessor, utcNow, stoppingToken);
                await RetryPendingProcessingAsync(sessionManager, profileManager, promptStore, postCloseProcessor, utcNow, stoppingToken);

                await storeCommitter.CommitAsync(stoppingToken);
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
    /// Finds active sessions that have exceeded their profile's inactivity timeout and closes them.
    /// </summary>
    private async Task CloseInactiveSessionsAsync(
        IAIChatSessionManager sessionManager,
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

            var queryContext = new AIChatSessionQueryContext { ProfileId = profile.ItemId };
            var page = 1;
            AIChatSessionResult result;

            do
            {
                result = await sessionManager.PageAsync(page, _pageSize, queryContext, cancellationToken);

                foreach (var entry in result.Sessions)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (entry.Status != ChatSessionStatus.Active || entry.LastActivityUtc >= cutoffUtc)
                    {
                        continue;
                    }

                    var chatSession = await sessionManager.FindByIdAsync(entry.SessionId, cancellationToken);

                    if (chatSession is null || chatSession.Status != ChatSessionStatus.Active)
                    {
                        continue;
                    }

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

                    await sessionManager.SaveAsync(chatSession, cancellationToken);

                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Closed inactive session '{SessionId}' for profile '{ProfileId}'. Post-processing: {NeedsProcessing}.",
                            chatSession.SessionId,
                            profile.ItemId,
                            chatSession.PostSessionProcessingStatus != PostSessionProcessingStatus.None);
                    }
                }

                page++;
            }
            while (result.Sessions.Any());
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
        var profiles = await profileManager.GetAsync(AIProfileType.Chat, cancellationToken);

        foreach (var profile in profiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var queryContext = new AIChatSessionQueryContext { ProfileId = profile.ItemId };
            var page = 1;
            AIChatSessionResult result;

            do
            {
                result = await sessionManager.PageAsync(page, _pageSize, queryContext, cancellationToken);

                foreach (var entry in result.Sessions)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (entry.Status != ChatSessionStatus.Closed)
                    {
                        continue;
                    }

                    var chatSession = await sessionManager.FindByIdAsync(entry.SessionId, cancellationToken);

                    if (chatSession is null)
                    {
                        continue;
                    }

                    if (chatSession.PostSessionProcessingStatus != PostSessionProcessingStatus.Pending)
                    {
                        continue;
                    }

                    if (chatSession.PostSessionProcessingAttempts >= AIChatSessionPostCloseProcessor.MaxPostCloseAttempts)
                    {
                        chatSession.PostSessionProcessingStatus = PostSessionProcessingStatus.Failed;
                        await sessionManager.SaveAsync(chatSession, cancellationToken);

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

                    var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
                    await postCloseProcessor.ProcessAsync(profile, chatSession, prompts, cancellationToken);
                    await sessionManager.SaveAsync(chatSession, cancellationToken);

                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Processed pending post-close work for session '{SessionId}'.",
                            chatSession.SessionId);
                    }
                }

                page++;
            }
            while (result.Sessions.Any());
        }
    }
}
