namespace CrestApps.Core.AI.Exceptions;

/// <summary>
/// Exception thrown when a new chat session cannot be started because the caller exceeded the
/// anonymous session-start rate limit. Derives from <see cref="InvalidOperationException"/> so callers
/// that only catch the base type still handle it (degrading to a generic error), while callers that
/// catch this specific type can surface a dedicated "session start rejected" signal to the client.
/// </summary>
public sealed class ChatSessionStartRateLimitedException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatSessionStartRateLimitedException"/> class.
    /// </summary>
    /// <param name="message">The user-facing message describing the throttle.</param>
    public ChatSessionStartRateLimitedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatSessionStartRateLimitedException"/> class.
    /// </summary>
    /// <param name="message">The user-facing message describing the throttle.</param>
    /// <param name="innerException">The inner exception.</param>
    public ChatSessionStartRateLimitedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
