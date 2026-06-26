using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Indexing;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tools;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Builders;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Extension methods for registering document processing and ingestion services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IngestionDocumentReader"/> implementation as a keyed singleton
    /// for each supported file extension.
    /// </summary>
    public static IServiceCollection AddCoreAIIngestionDocumentReader<T>(this IServiceCollection services, params ExtractorExtension[] supportedExtensions)
        where T : IngestionDocumentReader
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(supportedExtensions);

        services.Configure<ChatDocumentsOptions>(options =>
        {
            foreach (var extension in supportedExtensions)
            {
                options.Add(extension);
            }
        });

        services.TryAddSingleton<T>();

        foreach (var extension in supportedExtensions)
        {
            services.AddKeyedSingleton<IngestionDocumentReader>(
                extension.Extension,
                (sp, _) => sp.GetRequiredService<T>());
        }

        return services;
    }

    /// <summary>
    /// Adds the default document processing system tools and supporting services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<InteractionDocumentOptions>();
        services.AddOptions<DocumentFileSystemFileStoreOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<DocumentFileSystemFileStoreOptions>, DocumentFileSystemFileStoreOptionsConfiguration>());
        services.AddCoreAIDocumentIndexProfileHandler();
        services.TryAddSingleton<IAITextNormalizer, DefaultAITextNormalizer>();
        services.TryAddSingleton<IUploadedFileScanner, NoOpUploadedFileScanner>();
        services.TryAddSingleton<IDocumentFileStore>(sp =>
        {
            var basePath = sp.GetRequiredService<IOptions<DocumentFileSystemFileStoreOptions>>().Value.BasePath;

            return new FileSystemFileStore(basePath);
        });

        services.TryAddScoped<IAIDocumentProcessingService, DefaultAIDocumentProcessingService>();
        services.TryAddScoped<IImageAnalysisService, DefaultImageAnalysisService>();
        services.TryAddScoped<DefaultAIDocumentIndexingService>();
        services.TryAddScoped<ITabularBatchProcessor, TabularBatchProcessor>();
        services.TryAddSingleton<ITabularBatchResultCache, TabularBatchResultCache>();

        // In-memory tabular workspace + the built-in tabular data agent that queries it.
        services.AddOptions<TabularWorkspaceOptions>();
        services.TryAddSingleton<ITabularWorkspaceManager, TabularWorkspaceManager>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBuiltInAIAgentProvider, TabularDataAgentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIChatDocumentEventHandler, TabularWorkspaceDocumentEventHandler>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, DocumentChatInteractionSettingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, DocumentOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPreemptiveRagHandler, DocumentPreemptiveRagHandler>());

        services.AddCoreAIIngestionDocumentReader<PlainTextIngestionDocumentReader>(
            ".txt",
            new ExtractorExtension(".csv", false),
            ".md",
            ".json",
            ".xml",
            ".html",
            ".htm",
            ".log",
            ".yaml",
            ".yml");

        services.AddCoreAITool<SearchDocumentsTool>(SearchDocumentsTool.TheName)
            .WithTitle("Search Documents")
            .WithDescription("Searches uploaded or attached documents using semantic vector search.")
            .WithPurpose(AIToolPurposes.DocumentProcessing);

        services.AddCoreAITool<ReadDocumentTool>(ReadDocumentTool.TheName)
            .WithTitle("Read Document")
            .WithDescription("Reads the full text content of a specific document.")
            .WithPurpose(AIToolPurposes.DocumentProcessing);

        services.AddCoreAITool<ListTabularDataTool>(ListTabularDataTool.TheName)
            .WithTitle("List Tabular Data")
            .WithDescription("Lists the tabular tables (CSV, TSV, Excel) available in the conversation with their columns and row counts.")
            .WithCategory("Tabular Data")
            .Selectable();

        services.AddCoreAITool<QueryTabularDataTool>(QueryTabularDataTool.TheName)
            .WithTitle("Query Tabular Data")
            .WithDescription("Runs a read-only SQL query against uploaded tabular data and returns a compact result.")
            .WithCategory("Tabular Data")
            .Selectable();

        services.AddCoreAITool<ExecuteTabularCommandTool>(ExecuteTabularCommandTool.TheName)
            .WithTitle("Execute Tabular Command")
            .WithDescription("Applies a SQL manipulation (INSERT, UPDATE, DELETE, ALTER) to the in-memory copy of uploaded tabular data, preserving the original file.")
            .WithCategory("Tabular Data")
            .Selectable();

        services.AddCoreAITool<InspectImageTool>(InspectImageTool.TheName)
            .WithTitle("Inspect Image")
            .WithDescription("Performs detailed visual inspection of an uploaded image when text summaries are insufficient.")
            .WithPurpose(AIToolPurposes.DocumentProcessing);

        return services;
    }

    /// <summary>
    /// Adds document reference-link services so cited AI documents resolve to downloadable links.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIDocumentReferenceDownloads(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddKeyedScoped<IAIReferenceLinkResolver, DocumentAIReferenceLinkResolver>(AIReferenceTypes.DataSource.Document);

        return services;
    }

    /// <summary>
    /// Adds document processing.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsAISuiteBuilder AddDocumentProcessing(this CrestAppsAISuiteBuilder builder, Action<CrestAppsDocumentProcessingBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIDocumentProcessing();

        if (configure is not null)
        {
            configure(new CrestAppsDocumentProcessingBuilder(builder.Services));
        }

        return builder;
    }

    /// <summary>
    /// Adds document reference-link services so cited AI documents resolve to downloadable links.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsDocumentProcessingBuilder AddReferenceDownloads(this CrestAppsDocumentProcessingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIDocumentReferenceDownloads();

        return builder;
    }

    /// <summary>
    /// Adds core ai document index profile handler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIDocumentIndexProfileHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, AIDocumentSearchIndexProfileHandler>());

        return services;
    }
}
