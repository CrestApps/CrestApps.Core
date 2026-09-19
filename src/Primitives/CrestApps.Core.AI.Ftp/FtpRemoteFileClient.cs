using CrestApps.Core.AI.FileSources.FileTransfer;
using FluentFTP;

namespace CrestApps.Core.AI.Ftp;

/// <summary>
/// Lists and reads files over an open FTP connection.
/// </summary>
internal sealed class FtpRemoteFileClient : IRemoteFileClient
{
    private readonly AsyncFtpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="FtpRemoteFileClient"/> class.
    /// </summary>
    /// <param name="client">The connected client.</param>
    public FtpRemoteFileClient(AsyncFtpClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task<RemoteListing> ListAsync(string rootPath, bool recursive, int maxItems, CancellationToken cancellationToken = default)
    {
        var options = recursive ? FtpListOption.Recursive : FtpListOption.Auto;
        var listing = await _client.GetListing(rootPath, options, cancellationToken);
        var files = new List<RemoteFile>();
        var complete = true;

        foreach (var item in listing)
        {
            if (item.Type != FtpObjectType.File)
            {
                continue;
            }

            if (maxItems > 0 && files.Count >= maxItems)
            {
                complete = false;

                break;
            }

            var relative = ToRelative(rootPath, item.FullName);

            // An FTP server reports a modified time only when it supports MDTM, and many do not. A file
            // with neither a time nor a size is re-read every run rather than silently skipped.
            files.Add(new RemoteFile(
                relative,
                item.Size >= 0 ? item.Size : null,
                item.Modified == default ? null : new DateTimeOffset(item.Modified.ToUniversalTime(), TimeSpan.Zero)));
        }

        return new RemoteListing(files, complete, complete ? null : "More files are present than one run will take on.");
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(string rootPath, string path, CancellationToken cancellationToken = default)
    {
        var full = Combine(rootPath, path);
        var buffer = new MemoryStream();

        if (!await _client.DownloadStream(buffer, full, token: cancellationToken))
        {
            await buffer.DisposeAsync();

            return null;
        }

        buffer.Position = 0;

        return buffer;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _client.Disconnect();
        }
        catch (Exception)
        {
            // A connection that could not be closed cleanly is already gone.
        }

        _client.Dispose();
    }

    private static string ToRelative(string rootPath, string fullName)
    {
        var root = rootPath.TrimEnd('/');

        if (!string.IsNullOrEmpty(root) && fullName.StartsWith(root, StringComparison.Ordinal))
        {
            return fullName[root.Length..].TrimStart('/');
        }

        return fullName.TrimStart('/');
    }

    private static string Combine(string rootPath, string path)
    {
        return string.Concat(rootPath.TrimEnd('/'), "/", path.TrimStart('/'));
    }
}
