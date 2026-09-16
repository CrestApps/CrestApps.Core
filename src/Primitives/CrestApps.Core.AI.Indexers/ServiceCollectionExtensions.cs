using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Knowledge;
using CrestApps.Core.AI.Indexers.Connectors;
using CrestApps.Core.AI.Indexers.Handlers;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Builders;
using CrestApps.Core.DataIngestion;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Hosting;

namespace CrestApps.Core.AI.Indexers;

/// <summary>
/// Registers the indexer subsystem: connectors, the run service, and the options that bound both.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the indexer subsystem.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// Call this after <c>AddDocumentProcessing()</c>. Keyed readers resolve to the last registration, and
    /// the HTML reader registered here has to win over the plain-text reader document processing registers
    /// for <c>.html</c>, or a web page read from a folder or a server is indexed with its markup.
    /// </remarks>
    public static IServiceCollection AddCoreIndexers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<IndexerOptions>();
        services.AddOptions<IngestionConnectorOptions>();
        // Connectors are scoped, so the resolver has to be too: a singleton holding the root provider cannot
        // resolve a keyed scoped service and throws the first time an indexer runs.
        services.TryAddScoped<IIngestionConnectorResolver, KeyedIngestionConnectorResolver>();
        services.TryAddScoped<IIndexerRunService, DefaultIndexerRunService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<WebCrawler>, IndexerSettingsCatalogHandler>());

        // A connector hands over whatever a folder or a server holds, and a web page read that way is HTML
        // rather than the cleaned text a crawl strategy produces. The HTML reader takes those keys over from
        // the plain-text reader, which would otherwise index the markup.
        services.AddCoreAIIngestionDocumentReader<HtmlIngestionDocumentReader>(".html", ".htm");

        // Replaces the default that answers nothing, so a figure produced by an indexer is transcribed
        // by the model that indexer was configured with.
        services.Replace(ServiceDescriptor.Scoped<IKnowledgeVisionDeploymentResolver, IndexerVisionDeploymentResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, IndexerBackgroundService>());

        return services;
    }

    /// <summary>
    /// Registers one ingestion connector under its own name.
    /// </summary>
    /// <typeparam name="TConnector">The connector type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The connector name, which is stored as an indexer's source.</param>
    public static IServiceCollection AddCoreIngestionConnector<TConnector>(
        this IServiceCollection services,
        string name,
        Action<IngestionConnectorDescriptor> configure = null)
        where TConnector : class, IIngestionConnector
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        services.AddCoreIndexers();
        services.TryAddScoped<TConnector>();
        services.TryAddKeyedScoped<IIngestionConnector>(name, (sp, _) => sp.GetRequiredService<TConnector>());

        // Keyed services cannot be enumerated, so a screen that offers a choice of connectors needs the
        // registration to say the connector exists.
        services.Configure<IngestionConnectorOptions>(options =>
        {
            var descriptor = new IngestionConnectorDescriptor
            {
                Name = name,
                DisplayName = new LocalizedString(name, name),
                Description = new LocalizedString(name, name),
            };

            configure?.Invoke(descriptor);

            options.AddOrUpdate(descriptor.Name, descriptor.DisplayName, descriptor.Description);
        });

        return services;
    }

    /// <summary>
    /// Adds the local-folder connector, which reads files out of a folder on the host.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// A folder still has to be added to <see cref="IndexerOptions.AllowedLocalRoots"/> before anything can
    /// be read: registering the connector grants nothing on its own.
    /// </remarks>
    public static IServiceCollection AddCoreLocalFolderConnector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCoreIngestionConnector<LocalFolderIngestionConnector>(
            LocalFolderIngestionConnector.ConnectorName,
            descriptor =>
            {
                descriptor.DisplayName = new LocalizedString("LocalFolder", "Local folder");
                descriptor.Description = new LocalizedString("LocalFolder Description", "Reads files from a folder on the server, within the allowed roots.");
            });
    }

    /// <summary>
    /// Adds the indexer subsystem.
    /// </summary>
    /// <param name="builder">The AI suite builder.</param>
    /// <param name="configure">An optional callback that registers connectors.</param>
    public static CrestAppsAISuiteBuilder AddIndexers(this CrestAppsAISuiteBuilder builder, Action<IServiceCollection> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreIndexers();
        configure?.Invoke(builder.Services);

        return builder;
    }
}
