using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Indexing;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tools;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Orchestration;
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
    public static IServiceCollection AddCoreAIDocumentProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<InteractionDocumentOptions>();
        services.AddOptions<DocumentFileSystemFileStoreOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<DocumentFileSystemFileStoreOptions>, DocumentFileSystemFileStoreOptionsConfiguration>());
        services.AddCoreAIDocumentIndexProfileHandler();
        services.TryAddSingleton<IAITextNormalizer, DefaultAITextNormalizer>();
        services.TryAddSingleton<IDocumentFileStore>(sp =>
        {
            var basePath = sp.GetRequiredService<IOptions<DocumentFileSystemFileStoreOptions>>().Value.BasePath;

            return new FileSystemFileStore(basePath);
        });

        services.TryAddScoped<IAIDocumentProcessingService, DefaultAIDocumentProcessingService>();
        services.TryAddScoped<ITabularBatchProcessor, TabularBatchProcessor>();
        services.TryAddSingleton<ITabularBatchResultCache, TabularBatchResultCache>();

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

        services.AddCoreAITool<ReadTabularDataTool>(ReadTabularDataTool.TheName)
            .WithTitle("Read Tabular Data")
            .WithDescription("Reads and parses tabular data (CSV, TSV, Excel) from a document.")
            .WithPurpose(AIToolPurposes.DocumentProcessing);

        return services;
    }

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

    public static IServiceCollection AddCoreAIDocumentIndexProfileHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, AIDocumentSearchIndexProfileHandler>());

        return services;
    }
}
