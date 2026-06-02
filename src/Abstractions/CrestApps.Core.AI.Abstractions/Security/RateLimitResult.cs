namespace CrestApps.Core.AI.Security;

/// <summary>
/// Represents the result of a rate limit evaluation.
/// </summary>
public sealed class RateLimitResult
{
    /// <summary>
    /// A shared instance representing an allowed request.
    /// </summary>
    public static readonly RateLimitResult Allowed = new()
    {
        IsThrottled = false,
    };

    /// <summary>
    /// Gets a value indicating whether the request is throttled.
    /// </summary>
    public bool IsThrottled { get; init; }

    /// <summary>
    /// Gets the number of seconds remaining until the next request is allowed.
    /// Only populated when <see cref="IsThrottled"/> is <see langword="true"/>.
    /// </summary>
    public int RetryAfterSeconds { get; init; }

    /// <summary>
    /// Gets the number of messages sent in the current window.
    /// </summary>
    public int CurrentCount { get; init; }

    /// <summary>
    /// Gets the maximum messages allowed per window.
    /// </summary>
    public int MaxAllowed { get; init; }

    /// <summary>
    /// Creates a throttled result with the specified retry-after duration.
    /// </summary>
    /// <param name="retryAfterSeconds">The number of seconds until the next request is allowed.</param>
    /// <param name="currentCount">The current message count in the window.</param>
    /// <param name="maxAllowed">The maximum messages allowed per window.</param>
    public static RateLimitResult Throttled(int retryAfterSeconds, int currentCount, int maxAllowed)
    {
        return new RateLimitResult
        {
            IsThrottled = true,
            RetryAfterSeconds = retryAfterSeconds,
            CurrentCount = currentCount,
            MaxAllowed = maxAllowed,
        };
    }
}
