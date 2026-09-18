using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Sftp;
using CrestApps.Core.AI.Sftp.Models;
using CrestApps.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace CrestApps.Core.AI.Sftp;

/// <summary>
/// Reads files off an SFTP server.
/// </summary>
public sealed class SftpIngestionConnector : RemoteFileIngestionConnector
{
    /// <summary>
    /// The connector's registered name, stored as the indexer's source.
    /// </summary>
    public const string ConnectorName = "Sftp";

    /// <summary>
    /// Initializes a new instance of the <see cref="SftpIngestionConnector"/> class.
    /// </summary>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="logger">The logger.</param>
    public SftpIngestionConnector(SftpRemoteFileClientFactory clientFactory, ILogger<SftpIngestionConnector> logger)
        : base(clientFactory, logger)
    {
    }

    /// <inheritdoc />
    public override ValueTask ValidateAsync(WebCrawler settings, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(result);

        var metadata = settings.GetOrCreate<SftpConnectionMetadata>();

        if (string.IsNullOrWhiteSpace(metadata.Host))
        {
            result.Fail(new ValidationResult("A host is required.", [nameof(SftpConnectionMetadata.Host)]));
        }

        if (string.IsNullOrWhiteSpace(metadata.Username))
        {
            result.Fail(new ValidationResult("A user name is required.", [nameof(SftpConnectionMetadata.Username)]));
        }

        if (string.IsNullOrWhiteSpace(metadata.Password) && string.IsNullOrWhiteSpace(metadata.PrivateKey))
        {
            result.Fail(new ValidationResult(
                "Either a password or a private key is required.",
                [nameof(SftpConnectionMetadata.Password)]));
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Opens SFTP connections from an indexer's stored settings.
/// </summary>
public sealed class SftpRemoteFileClientFactory : IRemoteFileClientFactory
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<SftpRemoteFileClientFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SftpRemoteFileClientFactory"/> class.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider the secrets were encrypted with.</param>
    /// <param name="logger">The logger.</param>
    public SftpRemoteFileClientFactory(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SftpRemoteFileClientFactory> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ConnectorName => SftpIngestionConnector.ConnectorName;

    /// <inheritdoc />
    public Task<IRemoteFileClient> CreateAsync(WebCrawler indexer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexer);

        var metadata = indexer.GetOrCreate<SftpConnectionMetadata>();
        var password = Unprotect(metadata.Password, indexer.ItemId);
        var privateKey = Unprotect(metadata.PrivateKey, indexer.ItemId);

        ConnectionInfo connectionInfo;

        if (!string.IsNullOrEmpty(privateKey))
        {
            var passphrase = Unprotect(metadata.Passphrase, indexer.ItemId);

            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKey));

            var keyFile = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(keyStream)
                : new PrivateKeyFile(keyStream, passphrase);

            connectionInfo = new ConnectionInfo(
                metadata.Host,
                metadata.Port ?? 22,
                metadata.Username,
                new PrivateKeyAuthenticationMethod(metadata.Username, keyFile));
        }
        else
        {
            connectionInfo = new ConnectionInfo(
                metadata.Host,
                metadata.Port ?? 22,
                metadata.Username,
                new PasswordAuthenticationMethod(metadata.Username, password ?? string.Empty));
        }

        if (metadata.ConnectionTimeout is > 0)
        {
            connectionInfo.Timeout = TimeSpan.FromSeconds(metadata.ConnectionTimeout.Value);
        }

        var client = new SftpClient(connectionInfo);

        try
        {
            client.Connect();
        }
        catch
        {
            // A refused connection still leaves a client holding a socket. A background job that retries
            // every few minutes would otherwise leak one on every attempt, for as long as the credentials
            // stay wrong.
            client.Dispose();

            throw;
        }

        return Task.FromResult<IRemoteFileClient>(new SftpRemoteFileClient(client));
    }

    private string Unprotect(string value, string indexerId)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return _dataProtectionProvider.CreateProtector(SftpResourceConstants.DataProtectionPurpose).Unprotect(value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt an SFTP secret for indexer '{IndexerId}'.", indexerId);

            return null;
        }
    }
}

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
