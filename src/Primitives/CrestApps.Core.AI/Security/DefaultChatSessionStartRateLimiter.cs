using System.Collections.Concurrent;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Default implementation of <see cref="IChatSessionStartRateLimiter"/> that limits
/// anonymous session creation using a sliding window. Per-profile overrides on
/// <see cref="PromptSecurityProfileSettings"/> take precedence over the site-level defaults.
/// </summary>
public sealed class DefaultChatSessionStartRateLimiter : IChatSessionStartRateLimiter
{
    private readonly ConcurrentDictionary<string, SlidingWindowEntry> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<AIChatRateLimitingOptions> _rateLimitingOptions;
    private readonly IOptions<PromptSecurityOptions> _options;
    private readonly ILogger<DefaultChatSessionStartRateLimiter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultChatSessionStartRateLimiter"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="options">The prompt security options.</param>
    /// <param name="logger">The logger.</param>
    public DefaultChatSessionStartRateLimiter(
        TimeProvider timeProvider,
        IOptions<AIChatRateLimitingOptions> rateLimitingOptions,
        IOptions<PromptSecurityOptions> options,
        ILogger<DefaultChatSessionStartRateLimiter> logger)
    {
        _timeProvider = timeProvider;
        _rateLimitingOptions = rateLimitingOptions;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Determines whether the current session-start request should be rate-limited.
    /// </summary>
    /// <param name="context">The prompt security context identifying the visitor and request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<RateLimitResult> EvaluateAsync(PromptSecurityContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            return ValueTask.FromResult(RateLimitResult.Allowed);
        }

        var options = _options.Value;

        // Resolve per-profile anti-spam overrides, falling back to site-level defaults.
        var profileSettings = context.Profile?.TryGetSettings<PromptSecurityProfileSettings>(out var ps) == true ? ps : null;

        // Prefer the multi-tier limits; fall back to the single-window values only when no tiers
        // are configured (site or profile).
        var tiers = MultiTierRateLimitEvaluator.Normalize(
            profileSettings?.AnonymousSessionStartRateLimitTiers ?? options.AnonymousSessionStartRateLimitTiers);

        if (tiers.Count == 0)
        {
            var maxSessions = profileSettings?.MaxAnonymousSessionsPerWindow ?? options.MaxAnonymousSessionsPerWindow;
            var window = profileSettings?.AnonymousSessionRateLimitWindow ?? options.AnonymousSessionRateLimitWindow;

            tiers = MultiTierRateLimitEvaluator.Normalize([new ChatRateLimitTier(maxSessions, window)]);
        }

        if (tiers.Count == 0)
        {
            // Rate limiting disabled.
            return ValueTask.FromResult(RateLimitResult.Allowed);
        }

        var keys = ChatRateLimitKeyResolver.ResolveAnonymousSessionStartKeys(context, _rateLimitingOptions.Value);

        if (keys.Count == 0)
        {
            return ValueTask.FromResult(RateLimitResult.Allowed);
        }

        var now = _timeProvider.GetUtcNow();
        var result = MultiTierRateLimitEvaluator.Evaluate(_windows, keys, tiers, now);

        if (result.IsThrottled)
        {
            _logger.LogWarning(
                "Anonymous chat session start rate limit exceeded: Key={Key}, Count={Count}/{Max}, RetryAfter={RetryAfter}s",
                keys[0].SanitizeForLog(),
                result.CurrentCount,
                result.MaxAllowed,
                result.RetryAfterSeconds);
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Resets the rate-limit tracking state for the provided key.
    /// </summary>
    /// <param name="key">The rate-limit key to clear.</param>
    public void Reset(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            _windows.TryRemove(key, out _);
        }
    }
}
