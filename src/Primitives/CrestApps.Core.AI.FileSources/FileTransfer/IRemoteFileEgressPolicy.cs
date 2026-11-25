using System.Net;

namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// Decides which address an FTP or SFTP connection opens. The FTP and SFTP clients connect only to the address returned
/// here and never look the host name up again, so a name that changes its answer between the check and the connection
/// cannot move the connection somewhere else. The FTP client also sends every passive data connection to this address,
/// whatever address the server names in its <c>PASV</c> or <c>EPSV</c> answer, and an SFTP connection through a proxy
/// asks for the proxy's address here too.
/// </summary>
/// <remarks>
/// The default, <see cref="DefaultRemoteFileEgressPolicy"/>, resolves the name and refuses nothing. A host that must keep
/// these connections off its own network registers its own policy that resolves the name, checks the address and throws
/// when it is not allowed.
/// </remarks>
public interface IRemoteFileEgressPolicy
{
    /// <summary>
    /// Resolves the host stored with a connection and returns the address to connect to.
    /// </summary>
    /// <param name="host">The host name or address stored with the connection.</param>
    /// <param name="port">The port the connection opens on that host.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The address to connect to. The policy throws instead when the connection is not allowed.</returns>
    Task<IPAddress> ResolveAsync(string host, int port, CancellationToken cancellationToken = default);
}
