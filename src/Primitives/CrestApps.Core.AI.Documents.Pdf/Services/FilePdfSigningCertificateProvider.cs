using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Loads the signing certificate from the PKCS#12 file named by <see cref="PdfSigningOptions.CertificatePath"/>.
/// </summary>
internal sealed class FilePdfSigningCertificateProvider : IPdfSigningCertificateProvider, IDisposable
{
    private readonly PdfSigningOptions _options;
    private readonly Lock _lock = new();
    private X509Certificate2 _certificate;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilePdfSigningCertificateProvider"/> class.
    /// </summary>
    /// <param name="options">The signing options.</param>
    public FilePdfSigningCertificateProvider(IOptions<PdfSigningOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Returns the certificate, loading it on first use.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.CertificatePath) || !File.Exists(_options.CertificatePath))
        {
            return Task.FromResult<X509Certificate2>(null);
        }

        lock (_lock)
        {
            _certificate ??= X509CertificateLoader.LoadPkcs12FromFile(
                _options.CertificatePath,
                _options.CertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
        }

        return Task.FromResult(_certificate.HasPrivateKey ? _certificate : null);
    }

    /// <summary>
    /// Releases the loaded certificate.
    /// </summary>
    public void Dispose()
    {
        _certificate?.Dispose();
    }
}
