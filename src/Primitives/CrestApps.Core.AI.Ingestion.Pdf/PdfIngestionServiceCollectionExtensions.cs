using CrestApps.Core.AI.Ingestion.Pdf.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Ingestion.Pdf;

/// <summary>
/// Extension methods for registering the PDF reader on the ingestion path.
/// </summary>
public static class PdfIngestionServiceCollectionExtensions
{
    /// <summary>
    /// Adds the PDF reader, which turns a PDF into text, tables and figures.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// Reading a PDF and writing one are separate concerns and separate packages. This one only reads; the
    /// writer that turns generated content into a downloadable PDF lives in
    /// <c>CrestApps.Core.AI.Documents.Pdf</c> along with the rest of generated files.
    /// </remarks>
    public static IServiceCollection AddCoreAIPdfIngestion(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<PdfLayoutOptions>();
        services.AddCoreAIIngestionDocumentReader<PdfIngestionDocumentReader>(".pdf");

        return services;
    }

    /// <summary>
    /// Adds the PDF reader to the ingestion path.
    /// </summary>
    /// <param name="builder">The ingestion builder.</param>
    public static CrestAppsDocumentIngestionBuilder AddPdf(this CrestAppsDocumentIngestionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIPdfIngestion();

        return builder;
    }
}
