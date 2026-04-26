using CrestApps.Core.AI.Documents.Pdf.Services;
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
    public static IServiceCollection AddCoreAIPdfDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCoreAIIngestionDocumentReader<PdfIngestionDocumentReader>(".pdf");

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
