using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.AI.Ingestion.Pdf;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Provides extension methods for PDF Service Collection.
/// </summary>
public static class PdfServiceCollectionExtensions
{
    /// <summary>
    /// Adds core ai pdf document processing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// Registers both halves of PDF support: the reader, which lives on the ingestion path in
    /// <c>CrestApps.Core.AI.Ingestion.Pdf</c>, and the writer that turns generated content into a
    /// downloadable PDF. A host that only reads PDFs into a knowledge base takes the ingestion package on its
    /// own and leaves the writer, and the document processing it needs, behind.
    /// </remarks>
    public static IServiceCollection AddCoreAIPdfDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCoreAIPdfIngestion();

        // Register the PDF output writer so generated files can be downloaded as PDF documents.
        services.AddGeneratedFileWriter<PdfGeneratedFileWriter>(".pdf");

        return services;
    }

    /// <summary>
    /// Adds pdf.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsDocumentProcessingBuilder AddPdf(this CrestAppsDocumentProcessingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIPdfDocumentProcessing();

        return builder;
    }
}
