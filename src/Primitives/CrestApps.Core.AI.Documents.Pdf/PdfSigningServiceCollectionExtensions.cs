using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Extension methods that turn on digital signing for the PDF agent.
/// </summary>
public static class PdfSigningServiceCollectionExtensions
{
    /// <summary>
    /// Lets the PDF agent sign documents with a certificate the host configures.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the certificate and the signature details.</param>
    /// <remarks>
    /// Registers the default certificate provider, which loads a PKCS#12 file from
    /// <see cref="PdfSigningOptions.CertificatePath"/>. Register your own
    /// <see cref="IPdfSigningCertificateProvider"/> first to take the certificate from somewhere else.
    /// </remarks>
    public static IServiceCollection AddCoreAIPdfSigning(this IServiceCollection services, Action<PdfSigningOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<PdfSigningOptions>();

        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton<IPdfSigningCertificateProvider, FilePdfSigningCertificateProvider>();

        return services;
    }

    /// <summary>
    /// Lets the PDF agent sign documents with a certificate the host configures.
    /// </summary>
    /// <param name="builder">The document processing builder.</param>
    /// <param name="configure">Configures the certificate and the signature details.</param>
    public static CrestAppsDocumentProcessingBuilder AddPdfSigning(this CrestAppsDocumentProcessingBuilder builder, Action<PdfSigningOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIPdfSigning(configure);

        return builder;
    }
}
