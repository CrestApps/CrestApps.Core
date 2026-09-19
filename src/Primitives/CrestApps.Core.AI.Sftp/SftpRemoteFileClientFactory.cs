using System.Text;
using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Sftp.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace CrestApps.Core.AI.Sftp;

/// <summary>
/// Opens SFTP connections from a file source's stored settings.
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
    public Task<IRemoteFileClient> CreateAsync(IngestionSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var metadata = source.GetOrCreate<SftpConnectionMetadata>();
        var password = Unprotect(metadata.Password, source.ItemId);
        var privateKey = Unprotect(metadata.PrivateKey, source.ItemId);

        ConnectionInfo connectionInfo;

        if (!string.IsNullOrEmpty(privateKey))
        {
            var passphrase = Unprotect(metadata.Passphrase, source.ItemId);

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

    private string Unprotect(string value, string sourceId)
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
            _logger.LogWarning(ex, "Failed to decrypt an SFTP secret for file source '{FileSourceId}'.", sourceId);

            return null;
        }
    }
}
