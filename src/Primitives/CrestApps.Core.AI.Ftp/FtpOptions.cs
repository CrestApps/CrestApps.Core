using CrestApps.Core.AI.Ftp.Models;

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
    /// The message used when a connection would send credentials without encryption and the host has not allowed it.
    /// </summary>
    public const string UnencryptedNotAllowedMessage = "This FTP connection sends its credentials unencrypted (its encryption mode is not Explicit or Implicit), which the host has not allowed. Set the encryption mode to Explicit or Implicit, or set FtpOptions.AllowUnencrypted on the host for a server that offers no TLS.";

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

    /// <summary>
    /// Gets or sets a value indicating whether a connection with credentials may be made without encryption. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// With the encryption mode left unset, <c>None</c> or <c>Auto</c>, a connection can run in plain text, where no
    /// certificate is involved at all and the user name and password cross the network readable. By default such a
    /// connection is refused when it has a user name, and the encryption mode has to be <c>Explicit</c> or
    /// <c>Implicit</c>. An anonymous connection has no credentials to protect and is not affected. Turn this on only for
    /// a server on a network you control that offers no TLS.
    /// </remarks>
    public bool AllowUnencrypted { get; set; }

    /// <summary>
    /// Checks a connection against the host's options.
    /// </summary>
    /// <param name="metadata">The connection metadata.</param>
    /// <returns>The reason the connection is refused, or <see langword="null"/> when it is allowed.</returns>
    public string GetPolicyViolation(FtpConnectionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.ValidateAnyCertificate && !AllowAnyCertificate)
        {
            return AnyCertificateNotAllowedMessage;
        }

        if (!AllowUnencrypted &&
            !string.IsNullOrEmpty(metadata.Username) &&
            !(Enum.TryParse<FluentFTP.FtpEncryptionMode>(metadata.EncryptionMode, true, out var mode) &&
              mode is FluentFTP.FtpEncryptionMode.Explicit or FluentFTP.FtpEncryptionMode.Implicit))
        {
            return UnencryptedNotAllowedMessage;
        }

        return null;
    }
}
