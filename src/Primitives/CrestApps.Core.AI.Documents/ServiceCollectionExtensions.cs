using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Indexing;
using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Ingestion.Processors;
using CrestApps.Core.AI.Documents.Knowledge;
using CrestApps.Core.AI.Documents.Knowledge.Structure;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tools;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Builders;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Extensions;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
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

            // A connector knows the media type a server declared and often has no file name at all, so a
            // reader has to be reachable by type as well as by extension. Keyed registrations resolve
            // last-wins, which is how a later registration replaces an earlier one for a shared type such
            // as text/html.
            var mediaType = MediaTypeHelper.InferMediaType(extension.Extension, fallbackContentType: string.Empty);

            if (!string.IsNullOrEmpty(mediaType))
            {
                services.AddKeyedSingleton<IngestionDocumentReader>(
                    mediaType,
                    (sp, _) => sp.GetRequiredService<T>());
            }
        }

        return services;
    }

    /// <summary>
    /// Registers an ingestion processor. Processors run in registration order, so a host that registers its
    /// own before calling <see cref="AddCoreAIDocumentProcessing"/> runs ahead of the built-in ones.
    /// </summary>
    /// <typeparam name="T">The processor type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// Processors are scoped because the useful ones reach a model, and everything that reaches a model in
    /// this library is scoped. A singleton processor holding a scoped deployment manager is a captive
    /// dependency, which the host's own container validation refuses to build at all.
    /// </remarks>
    public static IServiceCollection AddCoreAIIngestionDocumentProcessor<T>(this IServiceCollection services)
        where T : AIDocumentIngestionProcessor
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IngestionDocumentProcessor, T>());

        return services;
    }

    /// <summary>
    /// Adds the default document processing system tools and supporting services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The pipeline holds the processors and the resolver holds the provider the readers come out of, so
        // both follow the processors' lifetime. Every consumer of the pipeline is itself scoped.
        services.TryAddScoped<IIngestionDocumentReaderResolver, DefaultIngestionDocumentReaderResolver>();
        services.TryAddScoped<IAIDocumentIngestionPipeline, DefaultAIDocumentIngestionPipeline>();

        // The built-in processors run in this order and each depends on the one before it: a caption decides
        // whether a figure is salient, and salience decides whether a figure is worth describing.
        services.AddOptions<CaptionPatternOptions>();
        services.AddOptions<FigureSalienceOptions>();
        services.AddMemoryCache();
        services.TryAddSingleton<IFigureCaptionCandidateDetector, DefaultFigureCaptionCandidateDetector>();
        services.TryAddSingleton<IFigureCaptionResolver, DefaultFigureCaptionResolver>();
        services.TryAddSingleton<IFigureDescriptionCache, MemoryFigureDescriptionCache>();
        services.AddCoreAIIngestionDocumentProcessor<FigureCaptionProcessor>();
        services.AddCoreAIIngestionDocumentProcessor<FigureSalienceProcessor>();
        services.AddCoreAIIngestionDocumentProcessor<FigureDescriptionProcessor>();

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

        // Typed knowledge: uploaded files become separate, individually retrievable objects in an
        // Ingested data source.
        services.AddOptions<KnowledgeIngestionOptions>();
        services.TryAddSingleton<IDocumentStructureAnalyzer, TocSeededStructureAnalyzer>();
        services.TryAddScoped<IPublicationMetadataExtractor, DefaultPublicationMetadataExtractor>();
        services.TryAddScoped<IKnowledgeIngestionService, DefaultKnowledgeIngestionService>();
        services.TryAddScoped<IKnowledgeVisionDeploymentResolver, NullKnowledgeVisionDeploymentResolver>();
        services.TryAddScoped<IFigureDescriptionBackfillService, DefaultFigureDescriptionBackfillService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FigureDescriptionBackfillBackgroundService>());
        services.TryAddKeyedScoped<IAIDataSourceSourceHandler, FileAIDataSourceSourceHandler>(AIDataSourceSourceTypes.File);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<KnowledgeObject>, KnowledgeObjectCatalogHandler>());
        services.Configure<AIDataSourceSourceOptions>(options => options.AddOrUpdate(
            AIDataSourceSourceTypes.File,
            new LocalizedString("File", "Files"),
            new LocalizedString("File Source Description", "A target for file sources. Configure the folders and file servers to read in the File Sources area; text, figures, charts and tables are each stored as their own searchable object.")));

        services.TryAddScoped<IAIDocumentProcessingService, DefaultAIDocumentProcessingService>();
        services.TryAddScoped<IImageAnalysisService, DefaultImageAnalysisService>();
        services.TryAddScoped<DefaultAIDocumentIndexingService>();
        services.TryAddScoped<ITabularBatchProcessor, TabularBatchProcessor>();
        services.TryAddSingleton<ITabularBatchResultCache, TabularBatchResultCache>();

        // Per-prompt tabular workspace options + the system tabular data agent that queries it.
        services.AddOptions<TabularWorkspaceOptions>();
        services.TryAddSingleton<ITabularDocumentArtifactStore, DocumentFileStoreTabularDocumentArtifactStore>();
        services.TryAddScoped<TabularDocumentArtifactFactory>();
        services.AddTemplatesFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIProfileProvider, TabularDataAgentProvider>());

        // Generated, downloadable files (tabular exports and chat file generation) share a writer
        // abstraction keyed by file extension plus a service that stores the file as an AIDocument.
        services.AddOptions<GeneratedFileWriterOptions>();
        services.TryAddSingleton<IGeneratedFileWriterResolver, GeneratedFileWriterResolver>();
        services.TryAddScoped<IGeneratedDocumentService, DefaultGeneratedDocumentService>();
        services.TryAddScoped<IConversationDocumentCleanupService, DefaultConversationDocumentCleanupService>();
        services.AddGeneratedFileWriter<DelimitedGeneratedFileWriter>(".csv");
        services.AddGeneratedFileWriter<PlainTextGeneratedFileWriter>(
            ".txt",
            ".md",
            ".json",
            ".xml",
            ".html",
            ".htm",
            ".yaml",
            ".yml",
            ".log");

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, DocumentChatInteractionSettingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, DocumentOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPreemptiveRagHandler, DocumentPreemptiveRagHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<ChatInteraction>, ChatInteractionDocumentCleanupHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionHistoryHandler, ChatInteractionGeneratedFileCleanupHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionHistoryHandler, TabularWorkspaceHistoryClearedHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIChatDocumentEventHandler, TabularWorkspaceDocumentEventHandler>());

        services.AddCoreAIIngestionDocumentReader<PlainTextIngestionDocumentReader>(
            ".txt",
            new ExtractorExtension(".csv", embeddable: false, isTabular: true),
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

        services.AddCoreAITool<GetDocumentMetadataTool>(GetDocumentMetadataTool.TheName)
            .WithTitle("Get Document Metadata")
            .WithDescription("Returns metadata for an attached document, including tabular headers, row counts, and normalized column names when applicable.")
            .WithPurpose(AIToolPurposes.DocumentProcessing);

        services.AddCoreAITool<ListTabularDataTool>(ListTabularDataTool.TheName)
            .WithTitle("List Tabular Data")
            .WithDescription("Lists the tabular tables available in the conversation with their columns and row counts.")
            .WithCategory("Tabular Data")
            .Hidden();

        services.AddCoreAITool<QueryTabularDataTool>(QueryTabularDataTool.TheName)
            .WithTitle("Query Tabular Data")
            .WithDescription("Runs a read-only SQL query against uploaded tabular data and returns a compact result.")
            .WithCategory("Tabular Data")
            .Hidden();

        services.AddCoreAITool<ExecuteTabularCommandTool>(ExecuteTabularCommandTool.TheName)
            .WithTitle("Execute Tabular Command")
            .WithDescription("Applies a SQL manipulation (INSERT, UPDATE, DELETE, ALTER) to the in-memory copy of uploaded tabular data, preserving the original file.")
            .WithCategory("Tabular Data")
            .Hidden();

        services.AddCoreAITool<FillEmptyTabularCellsTool>(FillEmptyTabularCellsTool.TheName)
            .WithTitle("Fill Empty Tabular Cells")
            .WithDescription("Replaces every empty cell in a loaded tabular table with a supplied value using one set-based update.")
            .WithCategory("Tabular Data")
            .Hidden();

        services.AddCoreAITool<FormatTabularDataTool>(FormatTabularDataTool.TheName)
            .WithTitle("Format Tabular Data")
            .WithDescription("Records the number formats, colors, conditional formatting, formulas, totals, and charts applied to an exported spreadsheet.")
            .WithCategory("Tabular Data")
            .Hidden();

        services.AddCoreAITool<ExportTabularDataTool>(ExportTabularDataTool.TheName)
            .WithTitle("Export Tabular Data")
            .WithDescription("Creates a downloadable file from a read-only SQL query over the active in-memory tabular workspace, preserving the original file format by default.")
            .WithCategory("Tabular Data")
            .Hidden();

        services.AddCoreAITool<CompareTabularDataTool>(CompareTabularDataTool.TheName)
            .WithTitle("Compare Tabular Data")
            .WithDescription("Compares a numeric measure between two tabular queries, matching rows on a shared key and reporting the difference.")
            .WithCategory("Tabular Data")
            .Hidden();

        services.AddCoreAITool<GenerateFileTool>(GenerateFileTool.TheName)
            .WithTitle("Generate File")
            .WithDescription("Creates a downloadable file (PDF, Word, Markdown, HTML, text, CSV, or spreadsheet) from generated content and attaches it to the conversation for download.")
            .WithPurpose(AIToolPurposes.ContentGeneration);

        services.AddCoreAITool<InspectImageTool>(InspectImageTool.TheName)
            .WithTitle("Inspect Image")
            .WithDescription("Performs detailed visual inspection of an uploaded image when text summaries are insufficient.")
            .WithPurpose(AIToolPurposes.DocumentProcessing);

        services.AddCoreAITool<ViewDocumentFigureTool>(ViewDocumentFigureTool.TheName)
            .WithTitle("View Document Figure")
            .WithDescription("Returns a figure from an attached document with a link that shows the picture in the answer, and can answer a question about the picture with a vision model.")
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
        services.AddKeyedScoped<IAIReferenceLinkResolver, FileReferenceLinkResolver>(AIDataSourceSourceTypes.File);

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
