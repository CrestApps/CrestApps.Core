using Azure.Search.Documents.Indexes;
using CrestApps.Core.Azure.AISearch.Builders;
using CrestApps.Core.Azure.AISearch.Services;
using CrestApps.Core.Builders;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Azure.AISearch;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core azure ai search services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    public static IServiceCollection AddCoreAzureAISearchServices(this IServiceCollection services, IConfigurationSection configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<AzureAISearchConnectionOptions>(configuration);

        return services.AddCoreAzureAISearchServices();
    }

    /// <summary>
    /// Adds core azure ai search services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// The services use the <see cref="SearchIndexClient"/> that <see cref="IAzureAISearchClientFactory"/> builds
    /// from <see cref="AzureAISearchConnectionOptions"/>. A host that supplies its own client calls
    /// <see cref="AddCoreAzureAISearchServices(IServiceCollection, Func{IServiceProvider, SearchIndexClient})"/>
    /// instead.
    /// </remarks>
    public static IServiceCollection AddCoreAzureAISearchServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddSingleton<IAzureAISearchClientFactory, AzureAISearchClientFactory>();
        services.TryAddSingleton(sp => sp.GetRequiredService<IAzureAISearchClientFactory>().CreateSearchIndexClient());

        return services.AddCoreAzureAISearchServices(static sp => sp.GetRequiredService<SearchIndexClient>());
    }

    /// <summary>
    /// Adds the core Azure AI Search services, reading the <see cref="SearchIndexClient"/> they use from
    /// <paramref name="clientFactory"/> instead of building it from <see cref="AzureAISearchConnectionOptions"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the client the services use. It runs each time one of the scoped services is created, so return a
    /// client the host already holds, such as the client of the current tenant, rather than building a new one.
    /// </param>
    /// <remarks>
    /// Registers the data source content manager, the data source document reader, the OData filter translator,
    /// the index manager and the document manager, all keyed by <see cref="AISearchConstants.ProviderName"/>.
    /// Neither <see cref="IAzureAISearchClientFactory"/> nor <see cref="SearchIndexClient"/> is registered. Each
    /// service is added only when no service is registered for its key yet. The index manager still reads its
    /// index prefix from <see cref="AzureAISearchConnectionOptions"/>.
    /// </remarks>
    public static IServiceCollection AddCoreAzureAISearchServices(this IServiceCollection services, Func<IServiceProvider, SearchIndexClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.AddOptions();
        services.AddAzureAISearchDataSourceContentManager(clientFactory);
        services.AddAzureAISearchDataSourceDocumentReader(clientFactory);
        services.AddAzureAISearchODataFilterTranslator();

        services.TryAddKeyedScoped<ISearchIndexManager>(AISearchConstants.ProviderName, (sp, _)
            => new AzureAISearchIndexManager(clientFactory(sp), sp.GetRequiredService<IOptions<AzureAISearchConnectionOptions>>(), sp.GetRequiredService<ILogger<AzureAISearchIndexManager>>()));

        services.TryAddKeyedScoped<ISearchDocumentManager>(AISearchConstants.ProviderName, (sp, _)
            => new AzureAISearchDocumentManager(clientFactory(sp), sp.GetServices<ISearchDocumentHandler>(), sp.GetRequiredService<ILogger<AzureAISearchDocumentManager>>()));

        return services;
    }

    /// <summary>
    /// Registers the Azure AI Search <see cref="IDataSourceContentManager"/>, keyed by
    /// <see cref="AISearchConstants.ProviderName"/>, which searches and deletes the rows of an AI knowledge base
    /// index through the client that <paramref name="clientFactory"/> returns.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the client the content manager uses. It runs each time the scoped content manager is created.
    /// </param>
    /// <remarks>
    /// The content manager is added only when none is registered for the key yet. Call this method on its own
    /// when the host registers its own index managers, for example from per-tenant search modules.
    /// </remarks>
    public static IServiceCollection AddAzureAISearchDataSourceContentManager(this IServiceCollection services, Func<IServiceProvider, SearchIndexClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.TryAddKeyedScoped<IDataSourceContentManager>(AISearchConstants.ProviderName, (sp, _)
            => new AzureAISearchDataSourceContentManager(clientFactory(sp), sp.GetRequiredService<ILogger<AzureAISearchDataSourceContentManager>>()));

        return services;
    }

    /// <summary>
    /// Registers the Azure AI Search <see cref="IDataSourceDocumentReader"/>, keyed by
    /// <see cref="AISearchConstants.ProviderName"/>, which reads the source documents of a data source from an
    /// index through the client that <paramref name="clientFactory"/> returns.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the client the document reader uses. It runs each time the scoped document reader is created.
    /// </param>
    /// <remarks>
    /// The document reader is added only when none is registered for the key yet.
    /// </remarks>
    public static IServiceCollection AddAzureAISearchDataSourceDocumentReader(this IServiceCollection services, Func<IServiceProvider, SearchIndexClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.TryAddKeyedScoped<IDataSourceDocumentReader>(AISearchConstants.ProviderName, (sp, _)
            => new DataSourceAzureAISearchDocumentReader(clientFactory(sp)));

        return services;
    }

    /// <summary>
    /// Registers only the Azure AI Search OData filter translator, keyed by
    /// <see cref="AISearchConstants.ProviderName"/>. The translator turns the OData filter of an AI profile,
    /// a chat interaction or a data source search into the Azure AI Search filter that data source retrieval runs.
    /// </summary>
    /// <remarks>
    /// <see cref="AddCoreAzureAISearchServices(IServiceCollection)"/> already calls this method. Call it directly
    /// when the host brings its own Azure AI Search clients, index managers and data source content manager and
    /// needs only the filter translation, so no other Azure AI Search service is registered.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddAzureAISearchODataFilterTranslator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddKeyedSingleton<IODataFilterTranslator>(AISearchConstants.ProviderName, (_, _)
            => new AzureAIODataFilterTranslator());

        return services;
    }

    /// <summary>
    /// Adds azure ai search.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsIndexingBuilder AddAzureAISearch(this CrestAppsIndexingBuilder builder, IConfigurationSection configuration, Action<CrestAppsAzureAISearchBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddCoreAzureAISearchServices(configuration);

        if (configure is not null)
        {
            configure(new CrestAppsAzureAISearchBuilder(builder.Services));
        }

        return builder;
    }

    /// <summary>
    /// Adds azure ai search.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsIndexingBuilder AddAzureAISearch(this CrestAppsIndexingBuilder builder, Action<CrestAppsAzureAISearchBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAzureAISearchServices();

        if (configure is not null)
        {
            configure(new CrestAppsAzureAISearchBuilder(builder.Services));
        }

        return builder;
    }
}
