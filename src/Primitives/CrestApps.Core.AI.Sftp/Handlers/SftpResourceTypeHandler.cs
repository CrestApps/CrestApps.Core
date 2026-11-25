using System.Text;
using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Sftp.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace CrestApps.Core.AI.Sftp.Handlers;

/// <summary>
/// Reads one file from an SFTP server for an MCP resource. The download goes through
/// <see cref="RemoteFileResourceHandlerBase{TMetadata}"/>, which caps how much of the file is buffered
/// (<see cref="SftpConnectionMetadata.MaxResourceBytes"/>, 5 MB by default), and the connection is only opened against a
/// server whose host key matches <see cref="SftpConnectionMetadata.HostKeyFingerprint"/>.
/// </summary>
public sealed class SftpResourceTypeHandler : RemoteFileResourceHandlerBase<SftpConnectionMetadata>
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IRemoteFileEgressPolicy _egressPolicy;
    private readonly ILogger<SftpResourceTypeHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SftpResourceTypeHandler"/> class.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="egressPolicy">The policy that resolves and checks the server's address, and the proxy's.</param>
    /// <param name="logger">The logger.</param>
    public SftpResourceTypeHandler(
        IDataProtectionProvider dataProtectionProvider,
        IRemoteFileEgressPolicy egressPolicy,
        ILogger<SftpResourceTypeHandler> logger)
        : base(SftpResourceConstants.Type, logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _egressPolicy = egressPolicy;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override string TransportName => "SFTP";

    /// <inheritdoc />
    protected override long? GetMaxResourceBytes(SftpConnectionMetadata metadata)
    {
        return metadata.MaxResourceBytes;
    }

    /// <inheritdoc />
    protected override bool TryGetHost(SftpConnectionMetadata metadata, out string host)
    {
        host = metadata?.Host;

        return !string.IsNullOrEmpty(host);
    }

    /// <inheritdoc />
    protected override string ValidateMetadata(SftpConnectionMetadata metadata)
    {
        // Fail closed: without a fingerprint to check, SSH.NET would trust whatever key the server presents, and the
        // credentials would go to anyone who intercepts the connection.
        if (!SftpHostKeys.IsConfigured(metadata.HostKeyFingerprint))
        {
            return SftpHostKeys.MissingFingerprintMessage;
        }

        if (string.IsNullOrEmpty(metadata.Password) && string.IsNullOrEmpty(metadata.PrivateKey))
        {
            return "No authentication method provided. Please provide a password or private key.";
        }

        return null;
    }

    /// <inheritdoc />
    protected override async Task DownloadAsync(SftpConnectionMetadata metadata, string host, string remotePath, Stream destination, CancellationToken cancellationToken)
    {
        var protector = _dataProtectionProvider.CreateProtector(SftpResourceConstants.DataProtectionPurpose);
        var username = metadata.Username;

        var password = DataProtectionHelper.Unprotect(protector, metadata.Password, _logger, "Failed to unprotect SFTP {FieldName} for host {Host}", "password", host);
        var privateKey = DataProtectionHelper.Unprotect(protector, metadata.PrivateKey, _logger, "Failed to unprotect SFTP {FieldName} for host {Host}", "private key", host);
        var passphrase = DataProtectionHelper.Unprotect(protector, metadata.Passphrase, _logger, "Failed to unprotect SFTP {FieldName} for host {Host}", "passphrase", host);
        var proxyPassword = DataProtectionHelper.Unprotect(protector, metadata.ProxyPassword, _logger, "Failed to unprotect SFTP {FieldName} for host {Host}", "proxy password", host);

        var authMethods = new List<AuthenticationMethod>();

        if (!string.IsNullOrEmpty(privateKey))
        {
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKey));
            var privateKeyFile = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(keyStream)
                : new PrivateKeyFile(keyStream, passphrase);
            authMethods.Add(new PrivateKeyAuthenticationMethod(username, privateKeyFile));
        }

        if (!string.IsNullOrEmpty(password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(username, password));
        }

        if (authMethods.Count == 0)
        {
            throw new InvalidOperationException("No authentication method could be read from the stored credentials.");
        }

        var connectionInfo = await SftpConnectionInfoFactory.CreateAsync(metadata, host, proxyPassword, authMethods.ToArray(), _egressPolicy, _logger, cancellationToken);

        if (metadata.ConnectionTimeout is > 0)
        {
            connectionInfo.Timeout = TimeSpan.FromSeconds(metadata.ConnectionTimeout.Value);
        }

        if (metadata.KeepAliveInterval is > 0)
        {
            connectionInfo.ChannelCloseTimeout = TimeSpan.FromSeconds(metadata.KeepAliveInterval.Value);
        }

        using var client = new SftpClient(connectionInfo);

        SftpHostKeys.Require(client, metadata.HostKeyFingerprint);

        if (metadata.KeepAliveInterval is > 0)
        {
            client.KeepAliveInterval = TimeSpan.FromSeconds(metadata.KeepAliveInterval.Value);
        }

        await Task.Run(client.Connect, cancellationToken);

        try
        {
            client.DownloadFile(remotePath, destination);
        }
        finally
        {
            client.Disconnect();
        }
    }
}
