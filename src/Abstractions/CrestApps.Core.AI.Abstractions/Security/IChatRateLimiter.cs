namespace CrestApps.Core.AI.Security;

/// <summary>
/// Provides rate limiting for AI chat messages to prevent abuse and brute-force prompt extraction.
/// Implementations track message frequency per session or user identity and reject requests
/// that exceed the configured threshold within a sliding time window.
/// </summary>
public interface IChatRateLimiter
{
    /// <summary>
    /// Determines whether the current request should be rate-limited based on configured thresholds.
    /// </summary>
    /// <param name="context">The prompt security context identifying the user, session, and connection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="RateLimitResult"/> indicating whether the request is allowed or throttled.
    /// </returns>
    ValueTask<RateLimitResult> EvaluateAsync(PromptSecurityContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the rate limit tracking state for a given session.
    /// Useful when an admin explicitly clears a user's rate limit.
    /// </summary>
    /// <param name="sessionId">The session identifier to reset.</param>
    void Reset(string sessionId);
}
