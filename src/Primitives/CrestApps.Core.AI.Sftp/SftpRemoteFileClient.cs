using CrestApps.Core.AI.FileSources.FileTransfer;
using Renci.SshNet;

namespace CrestApps.Core.AI.Sftp;

/// <summary>
/// Lists and reads files over an open SFTP connection.
/// </summary>
internal sealed class SftpRemoteFileClient : IRemoteFileClient
{
    private readonly SftpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="SftpRemoteFileClient"/> class.
    /// </summary>
    /// <param name="client">The connected client.</param>
    public SftpRemoteFileClient(SftpClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<RemoteListing> ListAsync(string rootPath, bool recursive, int maxItems, CancellationToken cancellationToken = default)
    {
        var files = new List<RemoteFile>();
        var complete = Walk(rootPath, rootPath, recursive, maxItems, files, cancellationToken);

        return Task.FromResult(new RemoteListing(
            files,
            complete,
            complete ? null : "More files are present than one run will take on."));
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string rootPath, string path, CancellationToken cancellationToken = default)
    {
        var full = Combine(rootPath, path);

        if (!_client.Exists(full))
        {
            return Task.FromResult<Stream>(null);
        }

        var buffer = new MemoryStream();

        _client.DownloadFile(full, buffer);

        buffer.Position = 0;

        return Task.FromResult<Stream>(buffer);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        try
        {
            _client.Disconnect();
        }
        catch (Exception)
        {
            // A connection that could not be closed cleanly is already gone.
        }

        _client.Dispose();

        return ValueTask.CompletedTask;
    }

    private bool Walk(
        string rootPath,
        string currentPath,
        bool recursive,
        int maxItems,
        List<RemoteFile> files,
        CancellationToken cancellationToken)
    {
        foreach (var entry in _client.ListDirectory(currentPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.Name is "." or "..")
            {
                continue;
            }

            if (entry.IsDirectory)
            {
                if (recursive && !Walk(rootPath, entry.FullName, recursive: true, maxItems, files, cancellationToken))
                {
                    return false;
                }

                continue;
            }

            if (!entry.IsRegularFile)
            {
                continue;
            }

            if (maxItems > 0 && files.Count >= maxItems)
            {
                return false;
            }

            files.Add(new RemoteFile(
                ToRelative(rootPath, entry.FullName),
                entry.Length,
                new DateTimeOffset(entry.LastWriteTimeUtc, TimeSpan.Zero)));
        }

        return true;
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
