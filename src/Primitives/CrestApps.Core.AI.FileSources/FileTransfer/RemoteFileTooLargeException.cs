namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// The exception thrown when a remote file is larger than the limit it is read with.
/// </summary>
public sealed class RemoteFileTooLargeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteFileTooLargeException"/> class.
    /// </summary>
    /// <param name="maxBytes">The limit that was exceeded.</param>
    public RemoteFileTooLargeException(long maxBytes)
        : base($"The remote file is larger than the {maxBytes:N0} bytes that are read.")
    {
        MaxBytes = maxBytes;
    }

    /// <summary>
    /// Gets the limit that was exceeded.
    /// </summary>
    public long MaxBytes { get; }
}
