using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.OpenXml;

/// <summary>
/// Provides extension methods for open Xml Service Collection.
/// </summary>
public static class OpenXmlServiceCollectionExtensions
{
    public static IServiceCollection AddCoreAIOpenXmlDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCoreAIIngestionDocumentReader<OpenXmlIngestionDocumentReader>(
            ".docx",
            new ExtractorExtension(".xlsx", false),
            ".pptx");

        return services;
    }

    public static CrestAppsDocumentProcessingBuilder AddOpenXml(this CrestAppsDocumentProcessingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIOpenXmlDocumentProcessing();
        return builder;
    }
}
