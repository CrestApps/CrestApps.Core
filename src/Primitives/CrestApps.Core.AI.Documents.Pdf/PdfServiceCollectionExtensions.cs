using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf;

public static class PdfServiceCollectionExtensions
{
    public static IServiceCollection AddCoreAIPdfDocumentProcessing(this IServiceCollection services)
    {
        services.AddCoreAIIngestionDocumentReader<PdfIngestionDocumentReader>(".pdf");

        return services;
    }

    public static CrestAppsDocumentProcessingBuilder AddPdf(this CrestAppsDocumentProcessingBuilder builder)
    {
        builder.Services.AddCoreAIPdfDocumentProcessing();
        return builder;
    }
}
