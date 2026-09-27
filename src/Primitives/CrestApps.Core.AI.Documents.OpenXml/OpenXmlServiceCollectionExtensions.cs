using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.OpenXml.Presentations;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            new ExtractorExtension(".xlsx", embeddable: false, isTabular: true),
            ".pptx",
            ".potx");
        services.AddSingleton<OpenXmlTabularDocumentArtifactBuilder>();
        services.AddSingleton<OpenXmlTabularWorkspaceImporter>();
        services.AddKeyedSingleton<ITabularDocumentArtifactBuilder>(
            ".xlsx",
            (sp, _) => sp.GetRequiredService<OpenXmlTabularDocumentArtifactBuilder>());
        services.AddKeyedSingleton<ITabularWorkspaceImporter>(
            ".xlsx",
            (sp, _) => sp.GetRequiredService<OpenXmlTabularWorkspaceImporter>());

        // Register Open XML output writers so generated files and tabular exports can target xlsx/docx/pptx.
        services.AddGeneratedFileWriter<SpreadsheetGeneratedFileWriter>(".xlsx");
        services.AddGeneratedFileWriter<WordGeneratedFileWriter>(".docx");
        services.AddGeneratedFileWriter<PresentationGeneratedFileWriter>(".pptx");

        // The presentation engine reads, edits and validates decks for the presentation agent; uploaded decks
        // and templates are imported through it, a template becoming an ordinary deck on the way in.
        services.TryAddSingleton<OpenXmlPresentationEngine>();
        services.TryAddSingleton<IPresentationEngine>(sp => sp.GetRequiredService<OpenXmlPresentationEngine>());
        services.TryAddKeyedSingleton<IPresentationImporter>(".pptx", (sp, _) => sp.GetRequiredService<OpenXmlPresentationEngine>());
        services.TryAddKeyedSingleton<IPresentationImporter>(".potx", (sp, _) => sp.GetRequiredService<OpenXmlPresentationEngine>());
        services.Configure<PresentationWorkspaceOptions>(options =>
        {
            options.AddExtension(".pptx");
            options.AddExtension(".potx");
        });

        services.AddCoreAIPresentationAgent();

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
