using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.OpenXml;

public static class OpenXmlServiceCollectionExtensions
{
    public static IServiceCollection AddCoreAIOpenXmlDocumentProcessing(this IServiceCollection services)
    {
        services.AddCoreAIIngestionDocumentReader<OpenXmlIngestionDocumentReader>(
            ".docx",
            new ExtractorExtension(".xlsx", false),
            ".pptx");

        return services;
    }

    public static CrestAppsDocumentProcessingBuilder AddOpenXml(this CrestAppsDocumentProcessingBuilder builder)
    {
        builder.Services.AddCoreAIOpenXmlDocumentProcessing();
        return builder;
    }
}
