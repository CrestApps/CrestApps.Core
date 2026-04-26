namespace CrestApps.Core.AI.Mcp.Ftp.Models;

/// <summary>
/// Represents the FTP Connection Metadata.
/// </summary>
public sealed class FtpConnectionMetadata
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
    /// Gets or sets the encryption Mode.
    /// </summary>
    public string EncryptionMode { get; set; }

    /// <summary>
    /// Gets or sets the data Connection Type.
    /// </summary>
    public string DataConnectionType { get; set; }

    /// <summary>
    /// Gets or sets the validate Any Certificate.
    /// </summary>
    public bool ValidateAnyCertificate { get; set; }

    /// <summary>
    /// Gets or sets the connect Timeout.
    /// </summary>
    public int? ConnectTimeout { get; set; }

    /// <summary>
    /// Gets or sets the read Timeout.
    /// </summary>
    public int? ReadTimeout { get; set; }

    /// <summary>
    /// Gets or sets the retry Attempts.
    /// </summary>
    public int? RetryAttempts { get; set; }
}
