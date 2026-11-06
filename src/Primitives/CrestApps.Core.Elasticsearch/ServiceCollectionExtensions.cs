using CrestApps.Core.Builders;
using CrestApps.Core.Elasticsearch.Builders;
using CrestApps.Core.Elasticsearch.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Elasticsearch;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core elasticsearch services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    public static IServiceCollection AddCoreElasticsearchServices(this IServiceCollection services, IConfigurationSection configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ElasticsearchConnectionOptions>(configuration);

        return services.AddCoreElasticsearchServices();
    }

    /// <summary>
    /// Adds core elasticsearch services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// The services use the <see cref="ElasticsearchClient"/> that <see cref="IElasticsearchClientFactory"/> builds
    /// from <see cref="ElasticsearchConnectionOptions"/>. A host that supplies its own client calls
    /// <see cref="AddCoreElasticsearchServices(IServiceCollection, Func{IServiceProvider, ElasticsearchClient})"/>
    /// instead.
    /// </remarks>
    public static IServiceCollection AddCoreElasticsearchServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddSingleton<IElasticsearchClientFactory, ElasticsearchClientFactory>();
        services.TryAddSingleton(sp => sp.GetRequiredService<IElasticsearchClientFactory>().Create());

        return services.AddCoreElasticsearchServices(static sp => sp.GetRequiredService<IElasticsearchClientFactory>().Create());
    }

    /// <summary>
    /// Adds the core Elasticsearch services, reading the <see cref="ElasticsearchClient"/> they use from
    /// <paramref name="clientFactory"/> instead of building it from <see cref="ElasticsearchConnectionOptions"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the client the services use. It runs each time one of the scoped services is created, so return a
    /// client the host already holds, such as the client of the current tenant, rather than building a new one.
    /// </param>
    /// <remarks>
    /// Registers the data source content manager, the data source document reader, the OData filter translator,
    /// the index manager and the document manager, all keyed by <see cref="ElasticsearchConstants.ProviderName"/>.
    /// Neither <see cref="IElasticsearchClientFactory"/> nor <see cref="ElasticsearchClient"/> is registered. Each
    /// service is added only when no service is registered for its key yet. The index manager still reads its
    /// index prefix from <see cref="ElasticsearchConnectionOptions"/>.
    /// </remarks>
    public static IServiceCollection AddCoreElasticsearchServices(this IServiceCollection services, Func<IServiceProvider, ElasticsearchClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.AddOptions();
        services.AddElasticsearchDataSourceContentManager(clientFactory);
        services.AddElasticsearchDataSourceDocumentReader(clientFactory);
        services.AddElasticsearchODataFilterTranslator();

        services.TryAddKeyedScoped<ISearchIndexManager>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchSearchIndexManager(clientFactory(sp), sp.GetRequiredService<IOptions<ElasticsearchConnectionOptions>>(), sp.GetRequiredService<ILogger<ElasticsearchSearchIndexManager>>()));

        services.TryAddKeyedScoped<ISearchDocumentManager>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchSearchDocumentManager(clientFactory(sp), sp.GetServices<ISearchDocumentHandler>(), sp.GetRequiredService<ILogger<ElasticsearchSearchDocumentManager>>()));

        return services;
    }

    /// <summary>
    /// Registers the Elasticsearch <see cref="IDataSourceContentManager"/>, keyed by
    /// <see cref="ElasticsearchConstants.ProviderName"/>, which searches and deletes the rows of an AI knowledge
    /// base index through the client that <paramref name="clientFactory"/> returns.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the client the content manager uses. It runs each time the scoped content manager is created.
    /// </param>
    /// <remarks>
    /// The content manager is added only when none is registered for the key yet. Call this method on its own
    /// when the host registers its own index managers, for example from per-tenant search modules.
    /// </remarks>
    public static IServiceCollection AddElasticsearchDataSourceContentManager(this IServiceCollection services, Func<IServiceProvider, ElasticsearchClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.TryAddKeyedScoped<IDataSourceContentManager>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchDataSourceContentManager(clientFactory(sp), sp.GetRequiredService<ILogger<ElasticsearchDataSourceContentManager>>()));

        return services;
    }

    /// <summary>
    /// Registers the Elasticsearch <see cref="IDataSourceDocumentReader"/>, keyed by
    /// <see cref="ElasticsearchConstants.ProviderName"/>, which reads the source documents of a data source from
    /// an index through the client that <paramref name="clientFactory"/> returns.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the client the document reader uses. It runs each time the scoped document reader is created.
    /// </param>
    /// <remarks>
    /// The document reader is added only when none is registered for the key yet.
    /// </remarks>
    public static IServiceCollection AddElasticsearchDataSourceDocumentReader(this IServiceCollection services, Func<IServiceProvider, ElasticsearchClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.TryAddKeyedScoped<IDataSourceDocumentReader>(ElasticsearchConstants.ProviderName, (sp, _)
            => new DataSourceElasticsearchDocumentReader(clientFactory(sp)));

        return services;
    }

    /// <summary>
    /// Registers only the Elasticsearch OData filter translator, keyed by
    /// <see cref="ElasticsearchConstants.ProviderName"/>. The translator turns the OData filter of an AI profile,
    /// a chat interaction or a data source search into the Elasticsearch query that data source retrieval runs.
    /// </summary>
    /// <remarks>
    /// <see cref="AddCoreElasticsearchServices(IServiceCollection)"/> already calls this method. Call it directly
    /// when the host brings its own Elasticsearch client, index managers and data source content manager and
    /// needs only the filter translation, so no other Elasticsearch service is registered.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddElasticsearchODataFilterTranslator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddKeyedSingleton<IODataFilterTranslator>(ElasticsearchConstants.ProviderName, (_, _)
            => new ElasticsearchODataFilterTranslator());

        return services;
    }

    /// <summary>
    /// Adds elasticsearch.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsIndexingBuilder AddElasticsearch(this CrestAppsIndexingBuilder builder, IConfigurationSection configuration, Action<CrestAppsElasticsearchBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddCoreElasticsearchServices(configuration);

        if (configure is not null)
        {
            configure(new CrestAppsElasticsearchBuilder(builder.Services));
        }

        return builder;
    }

    /// <summary>
    /// Adds elasticsearch.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsIndexingBuilder AddElasticsearch(this CrestAppsIndexingBuilder builder, Action<CrestAppsElasticsearchBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreElasticsearchServices();

        if (configure is not null)
        {
            configure(new CrestAppsElasticsearchBuilder(builder.Services));
        }

        return builder;
    }
}
