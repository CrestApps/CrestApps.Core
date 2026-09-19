namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// One file on a remote server.
/// </summary>
/// <param name="Path">The path, relative to the indexed folder.</param>
/// <param name="SizeBytes">How large the file is, when the server reports it.</param>
/// <param name="LastModifiedUtc">When the file last changed, when the server reports it.</param>
public sealed record RemoteFile(string Path, long? SizeBytes = null, DateTimeOffset? LastModifiedUtc = null);
