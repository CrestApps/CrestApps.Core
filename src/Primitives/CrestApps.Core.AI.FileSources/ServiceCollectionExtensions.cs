using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Knowledge;
using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.FileSources.Handlers;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Builders;
using CrestApps.Core.DataIngestion;
using CrestApps.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Hosting;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// Registers the file source subsystem: connectors, the run service, and the options that bound both.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The configuration section <see cref="FileSourceOptions"/> is read from.
    /// </summary>
    public const string ConfigurationSectionName = "CrestApps:AI:FileSources";

    /// <summary>
    /// The configuration section this feature was read from before it was renamed.
    /// </summary>
    /// <remarks>
    /// Kept readable because a configuration key lives in places this repository cannot see -- user
    /// secrets, environment variables, a deployed <c>appsettings.json</c>. Dropping it would take a host's
    /// allowed roots away silently, and a file source whose root is not allowed simply refuses every folder.
    /// </remarks>
    public const string DeprecatedConfigurationSectionName = "CrestApps:Indexers";

    /// <summary>
    /// Adds the file source subsystem and binds <see cref="FileSourceOptions"/> from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to read the options from.</param>
    /// <remarks>
    /// Reads <see cref="ConfigurationSectionName"/>, falling back to
    /// <see cref="DeprecatedConfigurationSectionName"/> when the new section is absent.
    /// <para>
    /// One section wins outright rather than the two being layered, because
    /// <see cref="FileSourceOptions.AllowedLocalRoots"/> is a list: binding both would merge them by index,
    /// so a host that shortened its list would keep entries it thought it had removed.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCoreFileSources(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ConfigurationSectionName);

        if (!section.Exists())
        {
            section = configuration.GetSection(DeprecatedConfigurationSectionName);
        }

        services.AddCoreFileSources();
        services.Configure<FileSourceOptions>(section);

        return services;
    }

    /// <summary>
    /// Adds the file source subsystem.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// Call this after <c>AddDocumentProcessing()</c>. Keyed readers resolve to the last registration, and
    /// the HTML reader registered here has to win over the plain-text reader document processing registers
    /// for <c>.html</c>, or a web page read from a folder or a server is indexed with its markup.
    /// </remarks>
    public static IServiceCollection AddCoreFileSources(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<FileSourceOptions>();
        services.AddOptions<IngestionConnectorOptions>();
        // Connectors are scoped, so the resolver has to be too: a singleton holding the root provider cannot
        // resolve a keyed scoped service and throws the first time an indexer runs.
        services.TryAddScoped<IIngestionConnectorResolver, KeyedIngestionConnectorResolver>();
        services.TryAddScoped<IFileSourceRunService, DefaultFileSourceRunService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<WebCrawler>, FileSourceSettingsCatalogHandler>());

        // A connector hands over whatever a folder or a server holds, and a web page read that way is HTML
        // rather than the cleaned text a crawl strategy produces. The HTML reader takes those keys over from
        // the plain-text reader, which would otherwise index the markup.
        services.AddCoreAIIngestionDocumentReader<HtmlIngestionDocumentReader>(".html", ".htm");

        // Replaces the default that answers nothing, so a figure produced by an indexer is transcribed
        // by the model that indexer was configured with.
        services.Replace(ServiceDescriptor.Scoped<IKnowledgeVisionDeploymentResolver, FileSourceVisionDeploymentResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FileSourceBackgroundService>());

        return services;
    }

    /// <summary>
    /// Registers one ingestion connector under its own name.
    /// </summary>
    /// <typeparam name="TConnector">The connector type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The connector name, which is stored as a file source's source.</param>
    /// <param name="configure">An optional callback that shapes how the connector is presented.</param>
    public static IServiceCollection AddCoreIngestionConnector<TConnector>(
        this IServiceCollection services,
        string name,
        Action<IngestionConnectorDescriptor> configure = null)
        where TConnector : class, IIngestionConnector
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        services.AddCoreFileSources();
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
    /// Adds the file-system connector, which reads files out of a folder on the host.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// A folder still has to be added to <see cref="FileSourceOptions.AllowedLocalRoots"/> before anything can
    /// be read: registering the connector grants nothing on its own.
    /// </remarks>
    public static IServiceCollection AddCoreFileSystemConnector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCoreIngestionConnector<FileSystemIngestionConnector>(
            FileSystemIngestionConnector.ConnectorName,
            descriptor =>
            {
                descriptor.DisplayName = new LocalizedString("FileSystem", "File system");
                descriptor.Description = new LocalizedString("FileSystem Description", "Reads files from a folder on the server, within the allowed roots.");
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

        builder.Services.AddCoreFileSources();
        configure?.Invoke(builder.Services);

        return builder;
    }
}
