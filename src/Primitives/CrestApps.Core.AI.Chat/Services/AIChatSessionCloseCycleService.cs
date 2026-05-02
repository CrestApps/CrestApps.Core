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
/// </summary>
public sealed class AIChatSessionCloseCycleService
{
    private static readonly TimeSpan _defaultInactivityTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMinutes(5);
    private const int _pageSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AIChatSessionCloseCycleService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIChatSessionCloseCycleService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public AIChatSessionCloseCycleService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AIChatSessionCloseCycleService> logger)
    {
        _scopeFactory = scopeFactory;
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
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sessionManager = scope.ServiceProvider.GetRequiredService<IAIChatSessionManager>();
            var profileManager = scope.ServiceProvider.GetRequiredService<IAIProfileManager>();
            var postCloseProcessor = scope.ServiceProvider.GetRequiredService<AIChatSessionPostCloseProcessor>();
            var promptStore = scope.ServiceProvider.GetRequiredService<IAIChatSessionPromptStore>();
            var storeCommitter = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();
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

                var entries = await ListSessionEntriesAsync(sessionManager, profile.ItemId, cancellationToken);
                var (profileClosedCount, profileAbandonedCount) = await CloseInactiveSessionsAsync(
                    sessionManager,
                    promptStore,
                    postCloseProcessor,
                    profile,
                    entries,
                    utcNow,
                    cancellationToken);

                closedCount += profileClosedCount;
                abandonedCount += profileAbandonedCount;

                var (profileRetriedCount, profileRecoveredCount, profileFailedCount) = await RetryPendingProcessingAsync(
                    sessionManager,
                    promptStore,
                    postCloseProcessor,
                    profile,
                    entries,
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

    private static async Task<List<AIChatSessionEntry>> ListSessionEntriesAsync(
        IAIChatSessionManager sessionManager,
        string profileId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentException.ThrowIfNullOrEmpty(profileId);

        var entries = new List<AIChatSessionEntry>();
        var queryContext = new AIChatSessionQueryContext
        {
            ProfileId = profileId,
        };
        var page = 1;

        while (true)
        {
            var result = await sessionManager.PageAsync(page, _pageSize, queryContext, cancellationToken);
            var pageEntries = result.Sessions.ToList();

            if (pageEntries.Count == 0)
            {
                break;
            }

            entries.AddRange(pageEntries);
            page++;
        }

        return entries;
    }

    private async Task<(int ClosedCount, int AbandonedCount)> CloseInactiveSessionsAsync(
        IAIChatSessionManager sessionManager,
        IAIChatSessionPromptStore promptStore,
        AIChatSessionPostCloseProcessor postCloseProcessor,
        AIProfile profile,
        IReadOnlyList<AIChatSessionEntry> entries,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(promptStore);
        ArgumentNullException.ThrowIfNull(postCloseProcessor);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(entries);

        var settings = profile.GetOrCreateSettings<AIProfileDataExtractionSettings>();
        var timeout = settings?.SessionInactivityTimeoutInMinutes > 0
            ? TimeSpan.FromMinutes(settings.SessionInactivityTimeoutInMinutes)
            : _defaultInactivityTimeout;
        var cutoffUtc = utcNow - timeout;
        var closedCount = 0;
        var abandonedCount = 0;

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
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

            await sessionManager.SaveAsync(chatSession, cancellationToken);

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
        IAIChatSessionManager sessionManager,
        IAIChatSessionPromptStore promptStore,
        AIChatSessionPostCloseProcessor postCloseProcessor,
        AIProfile profile,
        IReadOnlyList<AIChatSessionEntry> entries,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(promptStore);
        ArgumentNullException.ThrowIfNull(postCloseProcessor);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(entries);

        var retriedCount = 0;
        var recoveredCount = 0;
        var failedCount = 0;

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (entry.Status != ChatSessionStatus.Closed && entry.Status != ChatSessionStatus.Abandoned)
            {
                continue;
            }

            var chatSession = await sessionManager.FindByIdAsync(entry.SessionId, cancellationToken);

            if (chatSession is null)
            {
                continue;
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
                await sessionManager.SaveAsync(chatSession, cancellationToken);
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
            await sessionManager.SaveAsync(chatSession, cancellationToken);
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
