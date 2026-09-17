using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Ftp;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using FluentFTP;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace CrestApps.Core.AI.Ftp;

/// <summary>
/// Reads files off an FTP or FTPS server.
/// </summary>
public sealed class FtpIngestionConnector : RemoteFileIngestionConnector
{
    /// <summary>
    /// The connector's registered name, stored as the indexer's source.
    /// </summary>
    public const string ConnectorName = "Ftp";

    /// <summary>
    /// Initializes a new instance of the <see cref="FtpIngestionConnector"/> class.
    /// </summary>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="logger">The logger.</param>
    public FtpIngestionConnector(FtpRemoteFileClientFactory clientFactory, ILogger<FtpIngestionConnector> logger)
        : base(clientFactory, logger)
    {
    }

    /// <inheritdoc />
    public override ValueTask ValidateAsync(WebCrawler settings, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(result);

        var metadata = settings.GetOrCreate<FtpConnectionMetadata>();

        if (string.IsNullOrWhiteSpace(metadata.Host))
        {
            result.Fail(new ValidationResult("A host is required.", [nameof(FtpConnectionMetadata.Host)]));
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Opens FTP connections from an indexer's stored settings.
/// </summary>
public sealed class FtpRemoteFileClientFactory : IRemoteFileClientFactory
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<FtpRemoteFileClientFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FtpRemoteFileClientFactory"/> class.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider the password was encrypted with.</param>
    /// <param name="logger">The logger.</param>
    public FtpRemoteFileClientFactory(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<FtpRemoteFileClientFactory> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ConnectorName => FtpIngestionConnector.ConnectorName;

    /// <inheritdoc />
    public async Task<IRemoteFileClient> CreateAsync(WebCrawler indexer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexer);

        var metadata = indexer.GetOrCreate<FtpConnectionMetadata>();
        var client = new AsyncFtpClient(metadata.Host, metadata.Port ?? 21);

        if (!string.IsNullOrEmpty(metadata.Username))
        {
            client.Credentials = new NetworkCredential
            {
                UserName = metadata.Username,
                Password = Unprotect(metadata.Password, indexer.ItemId),
            };
        }

        if (Enum.TryParse<FtpEncryptionMode>(metadata.EncryptionMode, true, out var encryptionMode))
        {
            client.Config.EncryptionMode = encryptionMode;
        }

        if (Enum.TryParse<FtpDataConnectionType>(metadata.DataConnectionType, true, out var dataConnectionType))
        {
            client.Config.DataConnectionType = dataConnectionType;
        }

        if (metadata.ValidateAnyCertificate)
        {
            _logger.LogWarning("Indexer '{IndexerId}' accepts any FTP certificate. This disables TLS validation.", indexer.ItemId);
            client.ValidateCertificate += (_, args) => args.Accept = true;
        }

        if (metadata.ConnectTimeout is > 0)
        {
            client.Config.ConnectTimeout = metadata.ConnectTimeout.Value * 1000;
        }

        if (metadata.ReadTimeout is > 0)
        {
            client.Config.ReadTimeout = metadata.ReadTimeout.Value * 1000;
        }

        try
        {
            await client.Connect(cancellationToken);
        }
        catch
        {
            // A refused connection still leaves a client holding a socket. A background job that retries
            // every few minutes would otherwise leak one on every attempt, for as long as the credentials
            // stay wrong.
            client.Dispose();

            throw;
        }

        return new FtpRemoteFileClient(client);
    }

    private string Unprotect(string value, string indexerId)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return _dataProtectionProvider.CreateProtector(FtpResourceConstants.DataProtectionPurpose).Unprotect(value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt the FTP password for indexer '{IndexerId}'.", indexerId);

            return null;
        }
    }
}

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
