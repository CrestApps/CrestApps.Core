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
    /// <summary>
    /// Adds core ai open xml document processing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIOpenXmlDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCoreAIIngestionDocumentReader<OpenXmlIngestionDocumentReader>(
            ".docx",
            new ExtractorExtension(".xlsx", false),
            ".pptx");

        return services;
    }

    /// <summary>
    /// Adds open xml.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsDocumentProcessingBuilder AddOpenXml(this CrestAppsDocumentProcessingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIOpenXmlDocumentProcessing();

        return builder;
    }
}
