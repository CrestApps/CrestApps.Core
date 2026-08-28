using System.Collections.Concurrent;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Default implementation of <see cref="IChatRateLimiter"/> that enforces a sliding window
/// rate limit on AI chat messages. Messages are keyed by user identity (with fallback to
/// session or connection ID) and tracked using an in-memory sliding window queue.
/// </summary>
/// <remarks>
/// This implementation is designed for single-instance deployments. For distributed
/// deployments, replace with a Redis-backed or shared-state implementation.
/// </remarks>
public sealed class DefaultChatRateLimiter : IChatRateLimiter
{
    private readonly ConcurrentDictionary<string, SlidingWindowEntry> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<AIChatRateLimitingOptions> _rateLimitingOptions;
    private readonly IOptions<PromptSecurityOptions> _options;
    private readonly ILogger<DefaultChatRateLimiter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultChatRateLimiter"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="options">The prompt security options.</param>
    /// <param name="logger">The logger.</param>
    public DefaultChatRateLimiter(
        TimeProvider timeProvider,
        IOptions<AIChatRateLimitingOptions> rateLimitingOptions,
        IOptions<PromptSecurityOptions> options,
        ILogger<DefaultChatRateLimiter> logger)
    {
        _timeProvider = timeProvider;
        _rateLimitingOptions = rateLimitingOptions;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates whether the current request exceeds the configured rate limit.
    /// </summary>
    /// <param name="context">The prompt security context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<RateLimitResult> EvaluateAsync(PromptSecurityContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var siteOptions = _options.Value;
        var rateLimitingOptions = _rateLimitingOptions.Value;

        // Resolve per-profile rate limit overrides.
        var profileSettings = context.Profile?.TryGetSettings<PromptSecurityProfileSettings>(out var ps) == true ? ps : null;
        var maxMessages = profileSettings?.MaxMessagesPerWindow ?? siteOptions.MaxMessagesPerWindow;
        var window = profileSettings?.RateLimitWindow ?? siteOptions.RateLimitWindow;
        var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;

        // The anonymous tiers govern the anonymous message limit; they are also used (even for
        // authenticated callers) to size the retention of the shared network-address bucket so a
        // caller cannot shed their per-IP allowance by logging out.
        var anonymousTiers = MultiTierRateLimitEvaluator.Normalize(
            profileSettings?.AnonymousMessageRateLimitTiers ?? siteOptions.AnonymousMessageRateLimitTiers);
        var singleWindowTiers = MultiTierRateLimitEvaluator.Normalize([new ChatRateLimitTier { Limit = maxMessages, Window = window }]);

        var groups = new List<RateLimitKeyGroup>();

        if (isAuthenticated)
        {
            // Authenticated callers keep the single-window limit, applied to both their user identity
            // and their network address. The network-address bucket is shared with anonymous
            // throttling, so it is retained for at least as long as the anonymous tiers need.
            if (singleWindowTiers.Count > 0)
            {
                var userKey = ChatRateLimitKeyResolver.ResolveAuthenticatedUserKey(context);

                if (!string.IsNullOrEmpty(userKey))
                {
                    groups.Add(new RateLimitKeyGroup(
                        [userKey],
                        singleWindowTiers,
                        MultiTierRateLimitEvaluator.MaxWindow(singleWindowTiers)));
                }

                var contextKeys = ChatRateLimitKeyResolver.ResolveContextKeys(context, rateLimitingOptions.AuthenticatedMessagePartitions);

                if (contextKeys.Count > 0)
                {
                    var sharedRetention = MultiTierRateLimitEvaluator.MaxWindow(singleWindowTiers);
                    var anonymousRetention = MultiTierRateLimitEvaluator.MaxWindow(anonymousTiers);

                    if (anonymousRetention > sharedRetention)
                    {
                        sharedRetention = anonymousRetention;
                    }

                    groups.Add(new RateLimitKeyGroup(contextKeys, singleWindowTiers, sharedRetention));
                }
            }
        }
        else
        {
            // Anonymous callers use the multi-tier limits, falling back to the single window only
            // when no tiers are configured (site or profile).
            var tiers = anonymousTiers.Count > 0 ? anonymousTiers : singleWindowTiers;

            if (tiers.Count > 0)
            {
                var keys = ChatRateLimitKeyResolver.ResolveMessageKeys(context, rateLimitingOptions);

                if (keys.Count > 0)
                {
                    groups.Add(new RateLimitKeyGroup(keys, tiers, MultiTierRateLimitEvaluator.MaxWindow(tiers)));
                }
            }
        }

        // No enforceable group means rate limiting is disabled or no key could be resolved.
        if (groups.Count == 0)
        {
            return ValueTask.FromResult(RateLimitResult.Allowed);
        }

        var now = _timeProvider.GetUtcNow();
        var result = MultiTierRateLimitEvaluator.Evaluate(_windows, groups, now);

        if (result.IsThrottled)
        {
            _logger.LogWarning(
                "Rate limit exceeded: Key={Key}, Count={Count}/{Max}, RetryAfter={RetryAfter}s, Session={SessionId}",
                groups[0].Keys.Count > 0 ? groups[0].Keys[0].SanitizeForLog() : "unknown",
                result.CurrentCount,
                result.MaxAllowed,
                result.RetryAfterSeconds,
                context.SessionId.SanitizeForLog());
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Resets the rate limit tracking state for a given session.
    /// </summary>
    /// <param name="sessionId">The session identifier to reset.</param>
    public void Reset(string sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            _windows.TryRemove(sessionId, out _);
        }
    }
}
