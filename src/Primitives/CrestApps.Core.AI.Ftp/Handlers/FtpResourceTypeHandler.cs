using System.Net;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Mcp;
using FluentFTP;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Ftp.Handlers;

/// <summary>
/// Reads one file from an FTP or FTPS server for an MCP resource. The download goes through
/// <see cref="RemoteFileResourceHandlerBase{TMetadata}"/>, which caps how much of the file is buffered
/// (<see cref="FtpConnectionMetadata.MaxResourceBytes"/>, 5 MB by default), and a connection that asks to accept any
/// certificate is only honored when the host allows it (<see cref="FtpOptions.AllowAnyCertificate"/>).
/// </summary>
public sealed class FtpResourceTypeHandler : RemoteFileResourceHandlerBase<FtpConnectionMetadata>
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IOptions<FtpOptions> _options;
    private readonly ILogger<FtpResourceTypeHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FtpResourceTypeHandler"/> class.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="options">The host-level FTP options.</param>
    /// <param name="logger">The logger.</param>
    public FtpResourceTypeHandler(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<FtpOptions> options,
        ILogger<FtpResourceTypeHandler> logger)
        : base(FtpResourceConstants.Type, logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override string TransportName => "FTP";

    /// <inheritdoc />
    protected override long? GetMaxResourceBytes(FtpConnectionMetadata metadata)
    {
        return metadata.MaxResourceBytes;
    }

    /// <inheritdoc />
    protected override bool TryGetHost(FtpConnectionMetadata metadata, out string host)
    {
        host = metadata?.Host;

        return !string.IsNullOrEmpty(host);
    }

    /// <inheritdoc />
    protected override string ValidateMetadata(FtpConnectionMetadata metadata)
    {
        // The setting is stored data that whoever edits the resource controls, so it is only honored when the host
        // allowed it.
        if (metadata.ValidateAnyCertificate && !_options.Value.AllowAnyCertificate)
        {
            return FtpOptions.AnyCertificateNotAllowedMessage;
        }

        return null;
    }

    /// <inheritdoc />
    protected override async Task DownloadAsync(FtpConnectionMetadata metadata, string host, string remotePath, Stream destination, CancellationToken cancellationToken)
    {
        string password = null;

        if (!string.IsNullOrEmpty(metadata.Password))
        {
            try
            {
                password = _dataProtectionProvider.CreateProtector(FtpResourceConstants.DataProtectionPurpose).Unprotect(metadata.Password);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unprotect the FTP password for host {Host}", host);
            }
        }

        using var client = new AsyncFtpClient(host, metadata.Port ?? 21);

        if (!string.IsNullOrEmpty(metadata.Username))
        {
            client.Credentials = new NetworkCredential
            {
                UserName = metadata.Username,
                Password = password,
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
            _logger.LogWarning("FTP connection to '{Host}' is configured to accept any certificate. This disables TLS validation and should only be used in development.", host);
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

        if (metadata.RetryAttempts is > 0)
        {
            client.Config.RetryAttempts = metadata.RetryAttempts.Value;
        }

        await client.Connect(cancellationToken);

        try
        {
            await client.DownloadStream(destination, remotePath, token: cancellationToken);
        }
        finally
        {
            await client.Disconnect(cancellationToken);
        }
    }
}
