namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// The limits on what a remote file client reads into memory.
/// </summary>
public static class RemoteFileLimits
{
    /// <summary>
    /// The most bytes of one remote file ingestion reads when the connection sets no limit of its own: 100 MB, the same
    /// ceiling as an uploaded document.
    /// </summary>
    public const long DefaultMaxFileBytes = 100L * 1024 * 1024;
}
