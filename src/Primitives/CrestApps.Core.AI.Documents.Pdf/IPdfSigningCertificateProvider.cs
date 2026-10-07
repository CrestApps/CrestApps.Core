using System.Security.Cryptography.X509Certificates;

namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Supplies the certificate the PDF agent signs documents with.
/// </summary>
/// <remarks>
/// A signature is only as trustworthy as the identity behind it, so the agent never accepts a certificate
/// from the conversation: it signs with the identity the host configured, or not at all. Register an
/// implementation — or call <see cref="PdfSigningServiceCollectionExtensions.AddCoreAIPdfSigning"/> to load
/// one from a file — to turn signing on. Replace it to read the certificate from a key vault or a hardware
/// store instead.
/// </remarks>
public interface IPdfSigningCertificateProvider
{
    /// <summary>
    /// Returns the certificate, with its private key, that documents are signed with.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The certificate, or <see langword="null"/> when signing is not available.</returns>
    Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken = default);
}
