namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// One file on a remote server.
/// </summary>
/// <param name="Path">The path, relative to the indexed folder.</param>
/// <param name="SizeBytes">How large the file is, when the server reports it.</param>
/// <param name="LastModifiedUtc">When the file last changed, when the server reports it.</param>
public sealed record RemoteFile(string Path, long? SizeBytes = null, DateTimeOffset? LastModifiedUtc = null);

/// <summary>
/// What a remote listing found, and whether it found everything.
/// </summary>
/// <param name="Files">The files.</param>
/// <param name="IsComplete">Whether the listing is the whole of what the server holds under the folder.</param>
/// <param name="Message">Why the listing was incomplete, when it was.</param>
public sealed record RemoteListing(IReadOnlyList<RemoteFile> Files, bool IsComplete, string Message = null);

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

/// <summary>
/// Creates the client one indexer's settings describe.
/// </summary>
public interface IRemoteFileClientFactory
{
    /// <summary>
    /// Gets the connector name the factory serves.
    /// </summary>
    string ConnectorName { get; }

    /// <summary>
    /// Creates a client for the supplied indexer.
    /// </summary>
    /// <param name="indexer">The configured indexer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The client.</returns>
    Task<IRemoteFileClient> CreateAsync(CrestApps.Core.AI.Models.WebCrawler indexer, CancellationToken cancellationToken = default);
}
