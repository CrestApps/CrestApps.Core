namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// What a remote listing found, and whether it found everything.
/// </summary>
/// <param name="Files">The files.</param>
/// <param name="IsComplete">Whether the listing is the whole of what the server holds under the folder.</param>
/// <param name="Message">Why the listing was incomplete, when it was.</param>
public sealed record RemoteListing(IReadOnlyList<RemoteFile> Files, bool IsComplete, string Message = null);
