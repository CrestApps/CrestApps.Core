namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Configures how the PDF agent signs documents.
/// </summary>
public sealed class PdfSigningOptions
{
    /// <summary>
    /// Gets or sets the path of the PKCS#12 (<c>.pfx</c> or <c>.p12</c>) file holding the signing certificate
    /// and its private key. Used by the default certificate provider.
    /// </summary>
    public string CertificatePath { get; set; }

    /// <summary>
    /// Gets or sets the password of the certificate file.
    /// </summary>
    public string CertificatePassword { get; set; }

    /// <summary>
    /// Gets or sets the address of an RFC 3161 time-stamp authority. When set, each signature carries a
    /// trusted time stamp.
    /// </summary>
    public string TimestampAuthorityUrl { get; set; }

    /// <summary>
    /// Gets or sets the digest algorithm: <c>SHA256</c>, <c>SHA384</c> or <c>SHA512</c>. Defaults to
    /// <c>SHA256</c>.
    /// </summary>
    public string DigestAlgorithm { get; set; } = "SHA256";

    /// <summary>
    /// Gets or sets the reason recorded with a signature when the request gives none.
    /// </summary>
    public string DefaultReason { get; set; }

    /// <summary>
    /// Gets or sets the location recorded with a signature when the request gives none.
    /// </summary>
    public string DefaultLocation { get; set; }

    /// <summary>
    /// Gets or sets the contact information recorded with every signature.
    /// </summary>
    public string ContactInfo { get; set; }
}
