using CrestApps.Core.Builders;
using CrestApps.Core.Elasticsearch.Builders;
using CrestApps.Core.Elasticsearch.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
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
    public static IServiceCollection AddCoreElasticsearchServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddSingleton<IElasticsearchClientFactory, ElasticsearchClientFactory>();
        services.TryAddSingleton(sp => sp.GetRequiredService<IElasticsearchClientFactory>().Create());

        services.TryAddKeyedScoped<IDataSourceContentManager>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchDataSourceContentManager(sp.GetRequiredService<IElasticsearchClientFactory>().Create(), sp.GetRequiredService<ILogger<ElasticsearchDataSourceContentManager>>()));

        services.TryAddKeyedScoped<IDataSourceDocumentReader>(ElasticsearchConstants.ProviderName, (sp, _)
            => new DataSourceElasticsearchDocumentReader(sp.GetRequiredService<IElasticsearchClientFactory>().Create()));

        services.TryAddKeyedSingleton<IODataFilterTranslator>(ElasticsearchConstants.ProviderName, (_, _)
            => new ElasticsearchODataFilterTranslator());

        services.TryAddKeyedScoped<ISearchIndexManager>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchSearchIndexManager(sp.GetRequiredService<IElasticsearchClientFactory>().Create(), sp.GetRequiredService<IOptions<ElasticsearchConnectionOptions>>(), sp.GetRequiredService<ILogger<ElasticsearchSearchIndexManager>>()));

        services.TryAddKeyedScoped<ISearchDocumentManager>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchSearchDocumentManager(sp.GetRequiredService<IElasticsearchClientFactory>().Create(), sp.GetServices<ISearchDocumentHandler>(), sp.GetRequiredService<ILogger<ElasticsearchSearchDocumentManager>>()));

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
