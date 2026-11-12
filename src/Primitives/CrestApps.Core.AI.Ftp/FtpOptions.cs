namespace CrestApps.Core.AI.Ftp;

/// <summary>
/// Host-level options for the FTP and FTPS connectors.
/// </summary>
public sealed class FtpOptions
{
    /// <summary>
    /// The message used when a connection asks to accept any certificate and the host has not allowed it.
    /// </summary>
    public const string AnyCertificateNotAllowedMessage = "This FTP connection is set to accept any TLS certificate, which the host has not allowed. Turn the setting off so the server's certificate is validated, or set FtpOptions.AllowAnyCertificate on the host for a development server.";

    /// <summary>
    /// Gets or sets a value indicating whether a connection may be configured to accept any TLS certificate. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// A connection's own <c>ValidateAnyCertificate</c> setting is stored data that whoever edits the file source or
    /// resource controls, so it is only honored when the host has allowed it here. When it is not allowed, a connection
    /// that asks to accept any certificate is refused rather than silently validated, so the setting never looks like it
    /// works when it does not. Turn this on only for a development server with a self-signed certificate.
    /// </remarks>
    public bool AllowAnyCertificate { get; set; }
}
