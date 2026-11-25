using System.Net;

namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// Resolves the host of an FTP or SFTP connection and allows any address. A host name with several addresses is
/// connected through the first one the resolver returns.
/// </summary>
public sealed class DefaultRemoteFileEgressPolicy : IRemoteFileEgressPolicy
{
    /// <inheritdoc />
    public async Task<IPAddress> ResolveAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"The host '{host}' did not resolve to any address.");
        }

        return addresses[0];
    }
}
