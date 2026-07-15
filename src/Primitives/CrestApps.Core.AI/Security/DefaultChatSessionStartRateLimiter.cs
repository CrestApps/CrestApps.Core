using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Default implementation of <see cref="IChatSessionStartRateLimiter"/> that limits
/// anonymous session creation using a sliding window.
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
        var maxSessions = options.MaxAnonymousSessionsPerWindow;

        if (maxSessions <= 0)
        {
            return ValueTask.FromResult(RateLimitResult.Allowed);
        }

        var keys = ChatRateLimitKeyResolver.ResolveAnonymousSessionStartKeys(context, _rateLimitingOptions.Value);

        if (keys.Count == 0)
        {
            return ValueTask.FromResult(RateLimitResult.Allowed);
        }

        var window = options.AnonymousSessionRateLimitWindow;
        var now = _timeProvider.GetUtcNow();
        var windowStart = now - window;

        foreach (var key in keys)
        {
            var entry = _windows.GetOrAdd(key, static _ => new SlidingWindowEntry());

            lock (entry.Lock)
            {
                while (entry.Timestamps.Count > 0 && entry.Timestamps.Peek() <= windowStart)
                {
                    entry.Timestamps.Dequeue();
                }

                var currentCount = entry.Timestamps.Count;

                if (currentCount >= maxSessions)
                {
                    var oldestInWindow = entry.Timestamps.Peek();
                    var retryAfter = (int)Math.Ceiling((oldestInWindow + window - now).TotalSeconds);

                    if (retryAfter < 1)
                    {
                        retryAfter = 1;
                    }

                    _logger.LogWarning(
                        "Anonymous chat session start rate limit exceeded: Key={Key}, Count={Count}/{Max}, RetryAfter={RetryAfter}s",
                        key,
                        currentCount,
                        maxSessions,
                        retryAfter);

                    return ValueTask.FromResult(RateLimitResult.Throttled(retryAfter, currentCount, maxSessions));
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

    private sealed class SlidingWindowEntry
    {
        public object Lock { get; } = new();

        public Queue<DateTimeOffset> Timestamps { get; } = new();
    }
}
