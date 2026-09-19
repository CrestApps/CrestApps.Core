namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// The two things an ingestion connector needs from a file server.
/// </summary>
/// <remarks>
/// This seam exists so the connectors can be tested. An FTP or SSH client is a concrete class talking to a
/// real server, and a connector written directly against one can only be exercised by standing a server up.
/// The rules that matter here — what counts as changed, what happens when a listing fails — are the ones
/// worth testing, and none of them are about the protocol.
/// </remarks>
public interface IRemoteFileClient : IAsyncDisposable
{
    /// <summary>
    /// Lists the files under a folder.
    /// </summary>
    /// <param name="rootPath">The folder to list.</param>
    /// <param name="recursive">Whether sub-folders are listed too.</param>
    /// <param name="maxItems">The most files to return, or zero for no limit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was found, and whether that is all of it.</returns>
    Task<RemoteListing> ListAsync(string rootPath, bool recursive, int maxItems, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens one file for reading.
    /// </summary>
    /// <param name="rootPath">The indexed folder.</param>
    /// <param name="path">The file path, relative to the folder.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content, or <see langword="null"/> when the file is gone.</returns>
    Task<Stream> OpenReadAsync(string rootPath, string path, CancellationToken cancellationToken = default);
}
