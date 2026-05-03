using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Runs a single shared cycle that closes inactive AI chat sessions and retries pending post-close work.
/// Hosts can call this service directly when they need the framework logic without the default hosted runner.
/// Uses <see cref="IAIChatSessionStore"/> for unscoped data access, ensuring correct behavior
/// in background processing contexts where no user/HTTP context is available.
/// </summary>
public sealed class AIChatSessionCloseCycleService
{
    private static readonly TimeSpan _defaultInactivityTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AIChatSessionCloseCycleService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIChatSessionCloseCycleService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public AIChatSessionCloseCycleService(
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        ILogger<AIChatSessionCloseCycleService> logger)
    {
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs one AI chat session close cycle immediately.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionStore = _serviceProvider.GetRequiredService<IAIChatSessionStore>();
            var profileManager = _serviceProvider.GetRequiredService<IAIProfileManager>();
            var postCloseProcessor = _serviceProvider.GetRequiredService<AIChatSessionPostCloseProcessor>();
            var promptStore = _serviceProvider.GetRequiredService<IAIChatSessionPromptStore>();
            var storeCommitter = _serviceProvider.GetRequiredService<IStoreCommitter>();
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            var profiles = (await profileManager.GetAsync(AIProfileType.Chat, cancellationToken)).ToList();
            var closedCount = 0;
            var abandonedCount = 0;
            var retriedCount = 0;
            var recoveredCount = 0;
            var failedCount = 0;

            foreach (var profile in profiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var (profileClosedCount, profileAbandonedCount) = await CloseInactiveSessionsAsync(
                    sessionStore,
                    promptStore,
                    postCloseProcessor,
                    profile,
                    utcNow,
                    cancellationToken);

                closedCount += profileClosedCount;
                abandonedCount += profileAbandonedCount;

                var (profileRetriedCount, profileRecoveredCount, profileFailedCount) = await RetryPendingProcessingAsync(
                    sessionStore,
                    promptStore,
                    postCloseProcessor,
                    profile,
                    utcNow,
                    cancellationToken);

                retriedCount += profileRetriedCount;
                recoveredCount += profileRecoveredCount;
                failedCount += profileFailedCount;
            }

            await storeCommitter.CommitAsync(cancellationToken);

            if ((closedCount > 0
                || abandonedCount > 0
                || retriedCount > 0
                || recoveredCount > 0
                || failedCount > 0)
                && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "AI chat session close cycle completed. Profiles={ProfileCount}, Closed={ClosedCount}, Abandoned={AbandonedCount}, Retried={RetriedCount}, Recovered={RecoveredCount}, Failed={FailedCount}.",
                    profiles.Count,
                    closedCount,
                    abandonedCount,
                    retriedCount,
                    recoveredCount,
                    failedCount);
            }
            else if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "AI chat session close cycle completed with no work. Profiles evaluated: {ProfileCount}.",
                    profiles.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while closing inactive AI chat sessions.");
        }
    }

    private async Task<(int ClosedCount, int AbandonedCount)> CloseInactiveSessionsAsync(
        IAIChatSessionStore sessionStore,
        IAIChatSessionPromptStore promptStore,
        AIChatSessionPostCloseProcessor postCloseProcessor,
        AIProfile profile,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var settings = profile.GetOrCreateSettings<AIProfileDataExtractionSettings>();
        var timeout = settings?.SessionInactivityTimeoutInMinutes > 0
            ? TimeSpan.FromMinutes(settings.SessionInactivityTimeoutInMinutes)
            : _defaultInactivityTimeout;
        var cutoffUtc = utcNow - timeout;
        var closedCount = 0;
        var abandonedCount = 0;

        var inactiveSessions = await sessionStore.GetInactiveActiveSessionsAsync(profile.ItemId, cutoffUtc, cancellationToken);

        foreach (var chatSession in inactiveSessions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
            chatSession.Status = DetermineInactiveSessionStatus(prompts);
            chatSession.ClosedAtUtc = utcNow;

            if (postCloseProcessor.QueueIfNeeded(profile, chatSession))
            {
                await postCloseProcessor.ProcessAsync(profile, chatSession, prompts, cancellationToken);
            }
            else
            {
                chatSession.PostSessionProcessingStatus = PostSessionProcessingStatus.None;
            }

            await sessionStore.SaveAsync(chatSession, cancellationToken);

            if (chatSession.Status == ChatSessionStatus.Closed)
            {
                closedCount++;
            }
            else
            {
                abandonedCount++;
            }

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

        return (closedCount, abandonedCount);
    }

    private async Task<(int RetriedCount, int RecoveredCount, int FailedCount)> RetryPendingProcessingAsync(
        IAIChatSessionStore sessionStore,
        IAIChatSessionPromptStore promptStore,
        AIChatSessionPostCloseProcessor postCloseProcessor,
        AIProfile profile,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var retriedCount = 0;
        var recoveredCount = 0;
        var failedCount = 0;

        var closedSessions = await sessionStore.GetClosedSessionsAsync(profile.ItemId, cancellationToken);

        foreach (var chatSession in closedSessions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var originalStatus = chatSession.PostSessionProcessingStatus;
            var needsQueuedProcessing = postCloseProcessor.QueueIfNeeded(profile, chatSession);

            if (!needsQueuedProcessing)
            {
                continue;
            }

            if (originalStatus != PostSessionProcessingStatus.Pending)
            {
                recoveredCount++;

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Recovered closed session '{SessionId}' for post-close processing. Previous processing status was '{PreviousStatus}'.",
                        chatSession.SessionId,
                        originalStatus);
                }
            }

            if (chatSession.PostSessionProcessingAttempts >= postCloseProcessor.MaxPostCloseAttempts)
            {
                chatSession.PostSessionProcessingStatus = PostSessionProcessingStatus.Failed;
                await sessionStore.SaveAsync(chatSession, cancellationToken);
                failedCount++;

                _logger.LogWarning(
                    "Post-session processing for session '{SessionId}' failed after {MaxAttempts} attempts.",
                    chatSession.SessionId,
                    postCloseProcessor.MaxPostCloseAttempts);

                continue;
            }

            if (chatSession.PostSessionProcessingLastAttemptUtc.HasValue
                && (utcNow - chatSession.PostSessionProcessingLastAttemptUtc.Value) < _retryDelay)
            {
                continue;
            }

            var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
            await postCloseProcessor.ProcessAsync(profile, chatSession, prompts, cancellationToken);
            await sessionStore.SaveAsync(chatSession, cancellationToken);
            retriedCount++;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Processed pending post-close work for session '{SessionId}'.",
                    chatSession.SessionId);
            }
        }

        return (retriedCount, recoveredCount, failedCount);
    }

    private static ChatSessionStatus DetermineInactiveSessionStatus(IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);

        return prompts.Any(prompt => prompt.Role == ChatRole.User)
            ? ChatSessionStatus.Closed
            : ChatSessionStatus.Abandoned;
    }
}
