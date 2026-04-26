using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Provides extension methods for PDF Service Collection.
/// </summary>
public static class PdfServiceCollectionExtensions
{
    public static IServiceCollection AddCoreAIPdfDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCoreAIIngestionDocumentReader<PdfIngestionDocumentReader>(".pdf");

        return services;
    }

    public static CrestAppsDocumentProcessingBuilder AddPdf(this CrestAppsDocumentProcessingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIPdfDocumentProcessing();
        return builder;
    }
}
