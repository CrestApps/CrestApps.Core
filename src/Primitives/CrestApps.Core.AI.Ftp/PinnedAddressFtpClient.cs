using System.Net;
using FluentFTP;

namespace CrestApps.Core.AI.Ftp;

/// <summary>
/// An FTP client that opens every socket, the control connection and each passive data connection, to one address
/// that was checked before connecting. FluentFTP otherwise looks the host name up again when it connects, and opens a
/// passive data connection to whatever address the server names in its <c>PASV</c> answer (or in an <c>EPSV</c> answer
/// written the <c>PASV</c> way). The host name stays the client's <c>Host</c>, so TLS checks the server's certificate
/// against the name and sends it as the server name.
/// </summary>
internal sealed class PinnedAddressFtpClient : AsyncFtpClient
{
    private readonly string _address;

    /// <summary>
    /// Initializes a new instance of the <see cref="PinnedAddressFtpClient"/> class.
    /// </summary>
    /// <param name="host">The host name the connection was configured with.</param>
    /// <param name="port">The control connection port.</param>
    /// <param name="address">The checked address every connection opens to.</param>
    public PinnedAddressFtpClient(
        string host,
        int port,
        IPAddress address)
        : base(host, port)
    {
        _address = address.ToString();
    }

    /// <inheritdoc />
    protected override Task ConnectAsync(FtpSocketStream stream, CancellationToken token)
    {
        return stream.ConnectAsync(_address, Port, Config.InternetProtocolVersions, token);
    }

    /// <inheritdoc />
    protected override Task ConnectAsync(FtpSocketStream stream, string host, int port, FtpIpVersion ipVersions, CancellationToken token)
    {
        // FluentFTP calls this for a passive data connection, with the host it took from the server's answer. The port
        // is the server's to choose, but the connection stays on the server that was checked.
        return stream.ConnectAsync(_address, port, ipVersions, token);
    }
}
