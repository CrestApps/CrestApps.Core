using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenXml.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.OpenXml;

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
