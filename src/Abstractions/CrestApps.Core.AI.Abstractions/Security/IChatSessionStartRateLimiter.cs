namespace CrestApps.Core.AI.Security;

/// <summary>
/// Provides rate limiting for creating anonymous AI chat sessions.
/// </summary>
public interface IChatSessionStartRateLimiter
{
    /// <summary>
    /// Determines whether the current session-start request should be rate-limited.
    /// </summary>
    /// <param name="context">The prompt security context identifying the visitor and request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="RateLimitResult"/> indicating whether the request is allowed or throttled.
    /// </returns>
    ValueTask<RateLimitResult> EvaluateAsync(PromptSecurityContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the rate-limit tracking state for the provided key.
    /// </summary>
    /// <param name="key">The rate-limit key to clear.</param>
    void Reset(string key);
}
