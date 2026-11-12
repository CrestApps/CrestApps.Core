namespace CrestApps.Core.AI.Http;

/// <summary>
/// The exception thrown when a response body is larger than the limit it is read with.
/// </summary>
public sealed class HttpContentTooLargeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HttpContentTooLargeException"/> class.
    /// </summary>
    /// <param name="maxBytes">The limit that was exceeded.</param>
    public HttpContentTooLargeException(long maxBytes)
        : base($"The response is larger than the {maxBytes:N0} bytes that are read.")
    {
        MaxBytes = maxBytes;
    }

    /// <summary>
    /// Gets the limit that was exceeded.
    /// </summary>
    public long MaxBytes { get; }
}
