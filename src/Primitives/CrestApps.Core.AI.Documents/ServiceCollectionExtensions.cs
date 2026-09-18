using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Indexing;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tools;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Builders;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Extension methods for registering document processing and ingestion services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default document processing system tools and supporting services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// This is the chat-facing half of document processing: uploads, tabular workspaces, generated files and
    /// the tool surface. Everything that turns a file into knowledge lives in
    /// <c>CrestApps.Core.AI.Ingestion</c> and is registered by
    /// <see cref="ServiceCollectionExtensions.AddCoreAIDocumentIngestion"/>, which a host that only reads
    /// files into a knowledge base can call without any of this.
    /// </remarks>
    public static IServiceCollection AddCoreAIDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Chat uploads arrive as whatever a user attached, so document processing takes the whole ingestion
        // path: the plain-text reader, the figure processors that describe what a picture shows, and the
        // backfill that finishes the ones an upload left pending.
        services.AddCoreAIDocumentIngestion(ingestion => ingestion
            .AddPlainTextReader()
            .AddFigureProcessing()
            .AddFigureBackfill());

        services.AddOptions<InteractionDocumentOptions>();
        services.AddCoreAIDocumentIndexProfileHandler();
        services.TryAddSingleton<IUploadedFileScanner, NoOpUploadedFileScanner>();

        services.TryAddScoped<IAIDocumentProcessingService, DefaultAIDocumentProcessingService>();
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
