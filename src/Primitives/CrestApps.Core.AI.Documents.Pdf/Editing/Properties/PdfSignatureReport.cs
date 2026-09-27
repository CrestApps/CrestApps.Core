namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// What verifying one digital signature of a PDF found.
/// </summary>
internal sealed class PdfSignatureReport
{
    /// <summary>
    /// The status of a signature that verifies and covers the whole file.
    /// </summary>
    public const string Valid = "valid";

    /// <summary>
    /// The status of a signature that does not verify.
    /// </summary>
    public const string Invalid = "invalid";

    /// <summary>
    /// The status of a signature that verifies, over a file that was extended after it was signed.
    /// </summary>
    public const string ModifiedAfterSigning = "modified-after-signing";

    /// <summary>
    /// Gets or sets the signature field's name.
    /// </summary>
    public string FieldName { get; set; }

    /// <summary>
    /// Gets or sets the one-based page the signature's widget is on, when it has one.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the signature has a visible box.
    /// </summary>
    public bool Visible { get; set; }

    /// <summary>
    /// Gets or sets the signature format, the dictionary's <c>/SubFilter</c>.
    /// </summary>
    public string SubFilter { get; set; }

    /// <summary>
    /// Gets or sets the signer's name as the signature dictionary records it.
    /// </summary>
    public string SignerName { get; set; }

    /// <summary>
    /// Gets or sets the reason recorded with the signature.
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// Gets or sets the location recorded with the signature.
    /// </summary>
    public string Location { get; set; }

    /// <summary>
    /// Gets or sets when the signature was made, and where that time comes from.
    /// </summary>
    public string SigningTime { get; set; }

    /// <summary>
    /// Gets or sets the digest algorithm, such as <c>SHA256</c>.
    /// </summary>
    public string DigestAlgorithm { get; set; }

    /// <summary>
    /// Gets or sets the status: <see cref="Valid"/>, <see cref="Invalid"/> or <see cref="ModifiedAfterSigning"/>.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// Gets the reasons the signature does not verify, or notes about it.
    /// </summary>
    public List<string> Findings { get; } = [];

    /// <summary>
    /// Gets or sets the signer certificate's subject.
    /// </summary>
    public string CertificateSubject { get; set; }

    /// <summary>
    /// Gets or sets the signer certificate's issuer.
    /// </summary>
    public string CertificateIssuer { get; set; }

    /// <summary>
    /// Gets or sets the signer certificate's serial number.
    /// </summary>
    public string CertificateSerialNumber { get; set; }

    /// <summary>
    /// Gets or sets the signer certificate's validity period.
    /// </summary>
    public string CertificateValidity { get; set; }

    /// <summary>
    /// Gets or sets the signer certificate's SHA-1 thumbprint.
    /// </summary>
    public string CertificateThumbprint { get; set; }

    /// <summary>
    /// Gets or sets whether the certificate chains to a root this server trusts, or <see langword="null"/>
    /// when there was no certificate to check.
    /// </summary>
    public bool? Trusted { get; set; }

    /// <summary>
    /// Gets or sets why the certificate is not trusted.
    /// </summary>
    public string TrustDetail { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the signed byte ranges reach the end of the file.
    /// </summary>
    public bool CoversWholeFile { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes the file has after the end of the signed ranges.
    /// </summary>
    public long BytesAfter { get; set; }
}
