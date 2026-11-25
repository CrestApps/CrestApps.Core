using CrestApps.Core.AI.FileSources.FileTransfer;
using CrestApps.Core.AI.Sftp.Models;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace CrestApps.Core.AI.Sftp;

/// <summary>
/// Builds the SSH.NET <see cref="ConnectionInfo"/> for an SFTP connection from its stored settings. SSH.NET is given the
/// address the <see cref="IRemoteFileEgressPolicy"/> returned rather than the host name, so it cannot look the name up
/// again; the host key fingerprint still identifies the server. When the connection names a proxy, SSH.NET connects to
/// the proxy's checked address and asks the proxy for the server's checked address.
/// </summary>
internal static class SftpConnectionInfoFactory
{
    /// <summary>
    /// The message used when a connection names a proxy type without the proxy's host or port.
    /// </summary>
    public const string MissingProxyHostOrPortMessage = "This SFTP connection names a proxy type but not the proxy's host and port. Set both, or set the proxy type to None.";

    /// <summary>
    /// Resolves the server, and the proxy when one is set, through the egress policy and builds the connection info.
    /// </summary>
    /// <param name="metadata">The stored connection settings.</param>
    /// <param name="host">The server's host name.</param>
    /// <param name="proxyPassword">The proxy password, already unprotected.</param>
    /// <param name="authenticationMethods">The ways to authenticate with the server.</param>
    /// <param name="egressPolicy">The policy that resolves and checks each address.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connection info, before any timeouts are set.</returns>
    public static async Task<ConnectionInfo> CreateAsync(
        SftpConnectionMetadata metadata,
        string host,
        string proxyPassword,
        AuthenticationMethod[] authenticationMethods,
        IRemoteFileEgressPolicy egressPolicy,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var port = metadata.Port ?? 22;
        var proxyType = GetProxyType(metadata.ProxyType, logger);

        if (proxyType == ProxyTypes.None)
        {
            var address = await egressPolicy.ResolveAsync(host, port, cancellationToken);

            return new ConnectionInfo(address.ToString(), port, metadata.Username, authenticationMethods);
        }

        if (string.IsNullOrWhiteSpace(metadata.ProxyHost) || metadata.ProxyPort is not > 0)
        {
            throw new InvalidOperationException(MissingProxyHostOrPortMessage);
        }

        // The proxy is where the socket goes, and the server is where the proxy goes, so both are checked.
        var proxyAddress = await egressPolicy.ResolveAsync(metadata.ProxyHost, metadata.ProxyPort.Value, cancellationToken);
        var serverAddress = await egressPolicy.ResolveAsync(host, port, cancellationToken);

        return new ConnectionInfo(
            serverAddress.ToString(),
            port,
            metadata.Username,
            proxyType,
            proxyAddress.ToString(),
            metadata.ProxyPort.Value,
            metadata.ProxyUsername,
            proxyPassword,
            authenticationMethods);
    }

    private static ProxyTypes GetProxyType(string proxyType, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(proxyType))
        {
            return ProxyTypes.None;
        }

        if (Enum.TryParse<ProxyTypes>(proxyType, true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        logger.LogWarning("The SFTP proxy type '{ProxyType}' is not None, Socks4, Socks5 or Http, so the connection is made without a proxy.", proxyType);

        return ProxyTypes.None;
    }
}
