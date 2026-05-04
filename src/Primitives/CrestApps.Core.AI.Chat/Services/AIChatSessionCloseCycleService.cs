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

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AIChatSessionCloseCycleService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIChatSessionCloseCycleService"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public AIChatSessionCloseCycleService(
        TimeProvider timeProvider,
        ILogger<AIChatSessionCloseCycleService> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs one AI chat session close cycle immediately.
    /// </summary>
    /// <param name="serviceProvider">The root service provider used to create short-lived scopes per work unit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RunOnceAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        try
        {
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

            List<SessionWorkItem> workItems;

            using (var discoveryScope = serviceProvider.CreateScope())
            {
                workItems = await DiscoverWorkAsync(discoveryScope.ServiceProvider, utcNow, cancellationToken);
            }

            if (workItems.Count == 0)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("AI chat session close cycle completed with no work.");
                }

                return;
            }

            var closedCount = 0;
            var abandonedCount = 0;
            var retriedCount = 0;
            var recoveredCount = 0;
            var failedCount = 0;

            foreach (var workItem in workItems)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                using var processScope = serviceProvider.CreateScope();

                var result = await ProcessWorkItemAsync(processScope.ServiceProvider, workItem, utcNow, cancellationToken);

                closedCount += result.ClosedCount;
                abandonedCount += result.AbandonedCount;
                retriedCount += result.RetriedCount;
                recoveredCount += result.RecoveredCount;
                failedCount += result.FailedCount;
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "AI chat session close cycle completed. Sessions={SessionCount}, Closed={ClosedCount}, Abandoned={AbandonedCount}, Retried={RetriedCount}, Recovered={RecoveredCount}, Failed={FailedCount}.",
                    workItems.Count,
                    closedCount,
                    abandonedCount,
                    retriedCount,
                    recoveredCount,
                    failedCount);
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

    private static async Task<List<SessionWorkItem>> DiscoverWorkAsync(
        IServiceProvider serviceProvider,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var sessionStore = serviceProvider.GetRequiredService<IAIChatSessionStore>();
        var profileManager = serviceProvider.GetRequiredService<IAIProfileManager>();
        var postCloseProcessor = serviceProvider.GetRequiredService<AIChatSessionPostCloseProcessor>();
        var profiles = await profileManager.GetAsync(AIProfileType.Chat, cancellationToken);
        var workItems = new List<SessionWorkItem>();

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

            var inactiveSessions = await sessionStore.GetInactiveActiveSessionsAsync(profile.ItemId, cutoffUtc, cancellationToken);

            foreach (var session in inactiveSessions)
            {
                workItems.Add(new SessionWorkItem(profile.ItemId, session.SessionId, SessionWorkType.Close));
            }

            var closedSessions = await sessionStore.GetClosedSessionsAsync(profile.ItemId, cancellationToken);

            foreach (var session in closedSessions)
            {
                if (!postCloseProcessor.NeedsProcessing(profile, session))
                {
                    continue;
                }

                if (session.PostSessionProcessingAttempts >= postCloseProcessor.MaxPostCloseAttempts)
                {
                    workItems.Add(new SessionWorkItem(profile.ItemId, session.SessionId, SessionWorkType.MarkFailed));

                    continue;
                }

                if (session.PostSessionProcessingLastAttemptUtc.HasValue
                    && (utcNow - session.PostSessionProcessingLastAttemptUtc.Value) < _retryDelay)
                {
                    continue;
                }

                workItems.Add(new SessionWorkItem(profile.ItemId, session.SessionId, SessionWorkType.Retry));
            }
        }

        return workItems;
    }

    private async Task<WorkItemResult> ProcessWorkItemAsync(
        IServiceProvider serviceProvider,
        SessionWorkItem workItem,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var sessionStore = serviceProvider.GetRequiredService<IAIChatSessionStore>();
        var profileManager = serviceProvider.GetRequiredService<IAIProfileManager>();
        var postCloseProcessor = serviceProvider.GetRequiredService<AIChatSessionPostCloseProcessor>();
        var promptStore = serviceProvider.GetRequiredService<IAIChatSessionPromptStore>();
        var storeCommitter = serviceProvider.GetRequiredService<IStoreCommitter>();

        var profile = await profileManager.FindByIdAsync(workItem.ProfileId, cancellationToken);

        if (profile is null)
        {
            return default;
        }

        var chatSession = await sessionStore.FindByIdAsync(workItem.SessionId, cancellationToken);

        if (chatSession is null)
        {
            return default;
        }

        var result = new WorkItemResult();

        switch (workItem.WorkType)
        {
            case SessionWorkType.Close:
                await ProcessCloseAsync(sessionStore, promptStore, postCloseProcessor, profile, chatSession, utcNow, cancellationToken);
                result.ClosedCount = chatSession.Status == ChatSessionStatus.Closed ? 1 : 0;
                result.AbandonedCount = chatSession.Status == ChatSessionStatus.Abandoned ? 1 : 0;
                break;

            case SessionWorkType.Retry:
                await ProcessRetryAsync(sessionStore, promptStore, postCloseProcessor, profile, chatSession, cancellationToken);
                result.RetriedCount = 1;
                result.RecoveredCount = chatSession.PostSessionProcessingStatus == PostSessionProcessingStatus.Completed ? 1 : 0;
                break;

            case SessionWorkType.MarkFailed:
                chatSession.PostSessionProcessingStatus = PostSessionProcessingStatus.Failed;
                await sessionStore.SaveAsync(chatSession, cancellationToken);
                result.FailedCount = 1;

                _logger.LogWarning(
                    "Post-session processing for session '{SessionId}' failed after {MaxAttempts} attempts.",
                    chatSession.SessionId,
                    postCloseProcessor.MaxPostCloseAttempts);
                break;
        }

        await storeCommitter.CommitAsync(cancellationToken);

        return result;
    }

    private async Task ProcessCloseAsync(
        IAIChatSessionStore sessionStore,
        IAIChatSessionPromptStore promptStore,
        AIChatSessionPostCloseProcessor postCloseProcessor,
        AIProfile profile,
        AIChatSession chatSession,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
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

    private async Task ProcessRetryAsync(
        IAIChatSessionStore sessionStore,
        IAIChatSessionPromptStore promptStore,
        AIChatSessionPostCloseProcessor postCloseProcessor,
        AIProfile profile,
        AIChatSession chatSession,
        CancellationToken cancellationToken)
    {
        var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
        await postCloseProcessor.ProcessAsync(profile, chatSession, prompts, cancellationToken);
        await sessionStore.SaveAsync(chatSession, cancellationToken);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Processed pending post-close work for session '{SessionId}'.",
                chatSession.SessionId);
        }
    }

    private static ChatSessionStatus DetermineInactiveSessionStatus(IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);

        return prompts.Any(prompt => prompt.Role == ChatRole.User)
            ? ChatSessionStatus.Closed
            : ChatSessionStatus.Abandoned;
    }

    private enum SessionWorkType
    {
        Close,
        Retry,
        MarkFailed,
    }

    private readonly record struct SessionWorkItem(string ProfileId, string SessionId, SessionWorkType WorkType);

    private struct WorkItemResult
    {
        public int ClosedCount;
        public int AbandonedCount;
        public int RetriedCount;
        public int RecoveredCount;
        public int FailedCount;
    }
}
