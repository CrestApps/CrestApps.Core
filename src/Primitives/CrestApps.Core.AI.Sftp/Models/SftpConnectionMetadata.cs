namespace CrestApps.Core.AI.Sftp.Models;

/// <summary>
/// Represents the SFTP Connection Metadata.
/// </summary>
public sealed class SftpConnectionMetadata
{
    /// <summary>
    /// Gets or sets the host.
    /// </summary>
    public string Host { get; set; }

    /// <summary>
    /// Gets or sets the port.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// Gets or sets the private Key.
    /// </summary>
    public string PrivateKey { get; set; }

    /// <summary>
    /// Gets or sets the passphrase.
    /// </summary>
    public string Passphrase { get; set; }

    /// <summary>
    /// Gets or sets the fingerprint of the server's host key, such as <c>SHA256:...</c> as <c>ssh-keygen -lf</c> prints it, or several
    /// separated by commas. A connection is only opened when the server presents this key, and is refused when none is configured.
    /// </summary>
    public string HostKeyFingerprint { get; set; }

    /// <summary>
    /// Gets or sets the proxy Type.
    /// </summary>
    public string ProxyType { get; set; }

    /// <summary>
    /// Gets or sets the proxy Host.
    /// </summary>
    public string ProxyHost { get; set; }

    /// <summary>
    /// Gets or sets the proxy Port.
    /// </summary>
    public int? ProxyPort { get; set; }

    /// <summary>
    /// Gets or sets the proxy Username.
    /// </summary>
    public string ProxyUsername { get; set; }

    /// <summary>
    /// Gets or sets the proxy Password.
    /// </summary>
    public string ProxyPassword { get; set; }

    /// <summary>
    /// Gets or sets the connection Timeout.
    /// </summary>
    public int? ConnectionTimeout { get; set; }

    /// <summary>
    /// Gets or sets the keep Alive Interval.
    /// </summary>
    public int? KeepAliveInterval { get; set; }

    /// <summary>
    /// Gets or sets the most bytes an MCP resource read downloads from this server. A file larger than this is refused
    /// instead of being buffered. When not set, 5 MB applies.
    /// </summary>
    public long? MaxResourceBytes { get; set; }

    /// <summary>
    /// Gets or sets the most bytes of one file that ingestion reads from this server into memory. A larger file is not
    /// ingested. When not set, 100 MB applies.
    /// </summary>
    public long? MaxFileBytes { get; set; }
}
