using System.Net;
using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Models;
using FluentFTP;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Ftp;

/// <summary>
/// Opens FTP connections from a file source's stored settings.
/// </summary>
public sealed class FtpRemoteFileClientFactory : IRemoteFileClientFactory
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IOptions<FtpOptions> _options;
    private readonly ILogger<FtpRemoteFileClientFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FtpRemoteFileClientFactory"/> class.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider the password was encrypted with.</param>
    /// <param name="options">The host-level FTP options.</param>
    /// <param name="logger">The logger.</param>
    public FtpRemoteFileClientFactory(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<FtpOptions> options,
        ILogger<FtpRemoteFileClientFactory> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ConnectorName => FtpIngestionConnector.ConnectorName;

    /// <inheritdoc />
    public async Task<IRemoteFileClient> CreateAsync(IngestionSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var metadata = source.GetOrCreate<FtpConnectionMetadata>();

        // The certificate and encryption settings are stored data that whoever edits the file source controls, so they are only
        // honored when the host allowed them. Refusing is better than going on anyway: the failure then says why.
        var violation = _options.Value.GetPolicyViolation(metadata);

        if (violation is not null)
        {
            throw new InvalidOperationException(violation);
        }

        var client = new AsyncFtpClient(metadata.Host, metadata.Port ?? 21);

        if (!string.IsNullOrEmpty(metadata.Username))
        {
            client.Credentials = new NetworkCredential
            {
                UserName = metadata.Username,
                Password = Unprotect(metadata.Password, source.ItemId),
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
            _logger.LogWarning("File source '{FileSourceId}' accepts any FTP certificate. This disables TLS validation.", source.ItemId);
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

        return new FtpRemoteFileClient(client, metadata.MaxFileBytes is > 0 ? metadata.MaxFileBytes.Value : RemoteFileLimits.DefaultMaxFileBytes);
    }

    private string Unprotect(string value, string sourceId)
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
            _logger.LogWarning(ex, "Failed to decrypt the FTP password for file source '{FileSourceId}'.", sourceId);

            return null;
        }
    }
}
