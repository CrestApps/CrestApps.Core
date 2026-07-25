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

        // Resolve per-profile rate limit overrides.
        var profileSettings = context.Profile?.TryGetSettings<PromptSecurityProfileSettings>(out var ps) == true ? ps : null;
        var maxMessages = profileSettings?.MaxMessagesPerWindow ?? siteOptions.MaxMessagesPerWindow;
        var window = profileSettings?.RateLimitWindow ?? siteOptions.RateLimitWindow;

        // A max of zero means rate limiting is disabled.
        if (maxMessages <= 0)
        {
            return ValueTask.FromResult(RateLimitResult.Allowed);
        }

        var now = _timeProvider.GetUtcNow();
        var windowStart = now - window;
        var keys = ChatRateLimitKeyResolver.ResolveMessageKeys(context, _rateLimitingOptions.Value);

        if (keys.Count == 0)
        {
            return ValueTask.FromResult(RateLimitResult.Allowed);
        }

        foreach (var key in keys)
        {
            var entry = _windows.GetOrAdd(key, static _ => new SlidingWindowEntry());

            lock (entry.Lock)
            {
                // Evict timestamps outside the current window.
                while (entry.Timestamps.Count > 0 && entry.Timestamps.Peek() <= windowStart)
                {
                    entry.Timestamps.Dequeue();
                }

                var currentCount = entry.Timestamps.Count;

                if (currentCount >= maxMessages)
                {
                    // Calculate retry-after as the time until the oldest entry expires.
                    var oldestInWindow = entry.Timestamps.Peek();
                    var retryAfter = (int)Math.Ceiling((oldestInWindow + window - now).TotalSeconds);

                    if (retryAfter < 1)
                    {
                        retryAfter = 1;
                    }

                    _logger.LogWarning(
                        "Rate limit exceeded: Key={Key}, Count={Count}/{Max}, RetryAfter={RetryAfter}s, Session={SessionId}",
                        key.SanitizeForLog(),
                        currentCount,
                        maxMessages,
                        retryAfter,
                        context.SessionId.SanitizeForLog());

                    return ValueTask.FromResult(RateLimitResult.Throttled(retryAfter, currentCount, maxMessages));
                }
            }
        }

        foreach (var key in keys)
        {
            var entry = _windows.GetOrAdd(key, static _ => new SlidingWindowEntry());

            lock (entry.Lock)
            {
                entry.Timestamps.Enqueue(now);
            }
        }

        return ValueTask.FromResult(RateLimitResult.Allowed);
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

    private sealed class SlidingWindowEntry
    {
        public object Lock { get; } = new();

        public Queue<DateTimeOffset> Timestamps { get; } = new();
    }
}
