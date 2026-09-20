using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.AI.Ingestion.Processors;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Builders;
using CrestApps.Core.Services;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// Extension methods for registering the ingestion path: the readers that turn a file into a document, the
/// processors that enrich it, and the service that stores it as the typed knowledge of an AI data source.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services that run an ingestion source, shared by every feature that owns such records.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// File sources and web crawlers are separate features with separate stores, and either may be enabled
    /// without the other. What they have in common is the run itself: resolve a connector, list what the
    /// source holds, ingest what changed, record what happened. That lives here so neither feature has to
    /// depend on the other to get at it. Registration is idempotent, so both may call it.
    /// </remarks>
    public static IServiceCollection AddCoreAIIngestionRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<FileSourceOptions>();
        services.AddOptions<IngestionConnectorOptions>();

        // Connectors are scoped, so the resolver has to be too: a singleton holding the root provider cannot
        // resolve a keyed scoped service and throws the first time a source runs.
        services.TryAddScoped<IIngestionConnectorResolver, KeyedIngestionConnectorResolver>();
        services.TryAddScoped<IIngestionRunService, DefaultIngestionRunService>();

        // Replaces the default that answers nothing, so a figure produced by a source is transcribed by the
        // model that source was configured with, whichever feature owns the record.
        services.Replace(ServiceDescriptor.Scoped<IKnowledgeVisionDeploymentResolver, IngestionSourceVisionDeploymentResolver>());

        return services;
    }

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
    /// own before calling <see cref="AddCoreAIDocumentIngestion"/> runs ahead of the built-in ones.
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
    /// Adds the ingestion path: document readers, the processor pipeline, and the knowledge ingestion
    /// service that turns a file into the typed objects of an AI data source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional callback that registers the readers and processors this host wants.</param>
    /// <remarks>
    /// This is the half of document processing that has nothing to do with a chat conversation, and it is
    /// registered on its own so that a host which only reads files into a knowledge base does not also have
    /// to register uploads, tabular workspaces, generated files and the chat tool surface.
    /// <para>
    /// Only what cannot be left out is registered outright: the pipeline, the file store, and the knowledge
    /// service that writes objects. Readers and processors are asked for through <paramref name="configure"/>,
    /// so a host that ingests plain text never pays for a vision model and a host that reads no PDFs never
    /// carries a PDF reader. Every registration is a <c>TryAdd</c>, so calling it twice costs nothing.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddCoreAIDocumentIngestion(ingestion => ingestion
    ///     .AddPlainTextReader()
    ///     .AddFigureProcessing()
    ///     .AddFigureBackfill());
    /// </code>
    /// </example>
    public static IServiceCollection AddCoreAIDocumentIngestion(this IServiceCollection services, Action<CrestAppsDocumentIngestionBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The pipeline holds the processors and the resolver holds the provider the readers come out of, so
        // both follow the processors' lifetime. Every consumer of the pipeline is itself scoped.
        services.TryAddScoped<IIngestionDocumentReaderResolver, DefaultIngestionDocumentReaderResolver>();
        services.TryAddScoped<IAIDocumentIngestionPipeline, DefaultAIDocumentIngestionPipeline>();

        services.AddOptions<DocumentFileSystemFileStoreOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<DocumentFileSystemFileStoreOptions>, DocumentFileSystemFileStoreOptionsConfiguration>());

        // Registered by AddCoreAIServices() as well. Repeating it here is what lets the ingestion path be
        // added on its own, and TryAdd means the first registration still wins wherever both run.
        services.TryAddSingleton<IAITextNormalizer, DefaultAITextNormalizer>();

        // Not part of figure processing, because an upload endpoint reaches for it directly to describe a
        // picture a user attached, with nothing ingested at all.
        services.TryAddScoped<IImageAnalysisService, DefaultImageAnalysisService>();
        services.TryAddSingleton<IDocumentFileStore>(sp =>
        {
            var basePath = sp.GetRequiredService<IOptions<DocumentFileSystemFileStoreOptions>>().Value.BasePath;

            return new FileSystemFileStore(basePath);
        });

        // Typed knowledge: an ingested file becomes separate, individually retrievable objects in an
        // AI data source.
        services.AddOptions<KnowledgeIngestionOptions>();
        // The ladder, in the order of authority the strategies declare. A host adds a rung of its own by
        // registering another IDocumentStructureStrategy; it does not have to replace the analyzer to do it.
        services.TryAddSingleton<IDocumentStructureAnalyzer, DocumentStructureAnalyzer>();
        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<IDocumentStructureStrategy, OutlineStructureStrategy>(),
            ServiceDescriptor.Singleton<IDocumentStructureStrategy, StatedHeadingStructureStrategy>(),
            ServiceDescriptor.Singleton<IDocumentStructureStrategy, TableOfContentsStructureStrategy>(),
            ServiceDescriptor.Singleton<IDocumentStructureStrategy, InferredHeadingStructureStrategy>(),
        ]);
        services.TryAddScoped<IPublicationMetadataExtractor, DefaultPublicationMetadataExtractor>();
        services.TryAddScoped<IKnowledgeIngestionService, DefaultKnowledgeIngestionService>();
        services.TryAddScoped<IKnowledgeVisionDeploymentResolver, NullKnowledgeVisionDeploymentResolver>();

        // The read side of a knowledge data source, which is not the same thing as offering one in the admin
        // UI. Core retrieval and the knowledge tools resolve this handler to read objects back, so it is
        // registered for every host that can hold knowledge, whether or not anything here can still write it.
        // Whichever package fills a knowledge data source registers the source type itself.
        services.TryAddKeyedScoped<IAIDataSourceSourceHandler, FileAIDataSourceSourceHandler>(AIDataSourceSourceTypes.File);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<KnowledgeObject>, KnowledgeObjectCatalogHandler>());

        configure?.Invoke(new CrestAppsDocumentIngestionBuilder(services));

        return services;
    }

    /// <summary>
    /// Adds the built-in plain-text reader, which covers <c>.txt</c>, <c>.csv</c>, <c>.md</c>, <c>.json</c>,
    /// <c>.xml</c>, <c>.html</c>, <c>.htm</c>, <c>.log</c>, <c>.yaml</c> and <c>.yml</c>.
    /// </summary>
    /// <param name="builder">The ingestion builder.</param>
    /// <remarks>
    /// It claims the HTML extensions as well, and reads the markup as text. A host that fetches web pages
    /// registers a reader that cleans them afterwards, because keyed readers resolve to the last registration.
    /// </remarks>
    public static CrestAppsDocumentIngestionBuilder AddPlainTextReader(this CrestAppsDocumentIngestionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIIngestionDocumentReader<PlainTextIngestionDocumentReader>(
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

        return builder;
    }

    /// <summary>
    /// Adds the built-in figure processors, which caption the figures in a document, decide which of them
    /// carry meaning, and transcribe those with a vision model.
    /// </summary>
    /// <param name="builder">The ingestion builder.</param>
    /// <remarks>
    /// The three run in this order and each depends on the one before it: a caption decides whether a figure
    /// is salient, and salience decides whether a figure is worth describing. Leaving them out is how a host
    /// that ingests text it knows has no figures avoids reaching a vision model at all.
    /// </remarks>
    public static CrestAppsDocumentIngestionBuilder AddFigureProcessing(this CrestAppsDocumentIngestionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<CaptionPatternOptions>();
        builder.Services.AddOptions<FigureSalienceOptions>();
        builder.Services.AddMemoryCache();
        builder.Services.TryAddSingleton<IFigureCaptionCandidateDetector, DefaultFigureCaptionCandidateDetector>();
        builder.Services.TryAddSingleton<IFigureCaptionResolver, DefaultFigureCaptionResolver>();
        builder.Services.TryAddSingleton<IFigureDescriptionCache, MemoryFigureDescriptionCache>();
        builder.Services.AddCoreAIIngestionDocumentProcessor<FigureCaptionProcessor>();
        builder.Services.AddCoreAIIngestionDocumentProcessor<FigureSalienceProcessor>();
        builder.Services.AddCoreAIIngestionDocumentProcessor<FigureDescriptionProcessor>();

        return builder;
    }

    /// <summary>
    /// Adds the hosted job that transcribes the figures an ingest left pending.
    /// </summary>
    /// <param name="builder">The ingestion builder.</param>
    /// <remarks>
    /// Figures are never transcribed inline, so without this job a figure stored by an ingest stays pending
    /// and is never described. A host that runs its ingests elsewhere, or wants no background work in this
    /// process, leaves it out and calls <c>IFigureDescriptionBackfillService</c> on its own schedule.
    /// </remarks>
    public static CrestAppsDocumentIngestionBuilder AddFigureBackfill(this CrestAppsDocumentIngestionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddScoped<IFigureDescriptionBackfillService, DefaultFigureDescriptionBackfillService>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FigureDescriptionBackfillBackgroundService>());

        return builder;
    }
}
