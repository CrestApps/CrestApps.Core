using CrestApps.Core.Builders;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.PostgreSQL.Builders;
using CrestApps.Core.PostgreSQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.PostgreSQL;

/// <summary>
/// Provides extension methods for registering PostgreSQL indexing services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core PostgreSQL indexing services using the specified configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing PostgreSQL connection options.</param>
    public static IServiceCollection AddCorePostgreSQLServices(this IServiceCollection services, IConfigurationSection configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<PostgreSQLConnectionOptions>(configuration);

        return services.AddCorePostgreSQLServices();
    }

    /// <summary>
    /// Registers the core PostgreSQL indexing services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCorePostgreSQLServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddSingleton<IPostgreSQLClientFactory, PostgreSQLClientFactory>();

        services.TryAddKeyedScoped<IDataSourceContentManager>(PostgreSQLConstants.ProviderName, (sp, _)
            => new PostgreSQLDataSourceContentManager(sp.GetRequiredService<IPostgreSQLClientFactory>(), sp.GetRequiredService<ILogger<PostgreSQLDataSourceContentManager>>()));

        services.TryAddKeyedScoped<IDataSourceDocumentReader>(PostgreSQLConstants.ProviderName, (sp, _)
            => new DataSourcePostgreSQLDocumentReader(sp.GetRequiredService<IPostgreSQLClientFactory>()));

        services.TryAddKeyedSingleton<IODataFilterTranslator>(PostgreSQLConstants.ProviderName, (_, _)
            => new PostgreSQLODataFilterTranslator());

        services.TryAddKeyedScoped<ISearchIndexManager>(PostgreSQLConstants.ProviderName, (sp, _)
            => new PostgreSQLSearchIndexManager(sp.GetRequiredService<IPostgreSQLClientFactory>(), sp.GetRequiredService<IOptions<PostgreSQLConnectionOptions>>(), sp.GetRequiredService<ILogger<PostgreSQLSearchIndexManager>>()));

        services.TryAddKeyedScoped<ISearchDocumentManager>(PostgreSQLConstants.ProviderName, (sp, _)
            => new PostgreSQLSearchDocumentManager(sp.GetRequiredService<IPostgreSQLClientFactory>(), sp.GetServices<ISearchDocumentHandler>(), sp.GetRequiredService<ILogger<PostgreSQLSearchDocumentManager>>()));

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL as an indexing provider to the indexing builder with configuration from the specified section.
    /// </summary>
    /// <param name="builder">The indexing builder.</param>
    /// <param name="configuration">The configuration section containing PostgreSQL connection options.</param>
    /// <param name="configure">An optional action to configure additional PostgreSQL services.</param>
    public static CrestAppsIndexingBuilder AddPostgreSQL(this CrestAppsIndexingBuilder builder, IConfigurationSection configuration, Action<CrestAppsPostgreSQLBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddCorePostgreSQLServices(configuration);

        if (configure is not null)
        {
            configure(new CrestAppsPostgreSQLBuilder(builder.Services));
        }

        return builder;
    }

    /// <summary>
    /// Adds PostgreSQL as an indexing provider to the indexing builder.
    /// </summary>
    /// <param name="builder">The indexing builder.</param>
    /// <param name="configure">An optional action to configure additional PostgreSQL services.</param>
    public static CrestAppsIndexingBuilder AddPostgreSQL(this CrestAppsIndexingBuilder builder, Action<CrestAppsPostgreSQLBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCorePostgreSQLServices();

        if (configure is not null)
        {
            configure(new CrestAppsPostgreSQLBuilder(builder.Services));
        }

        return builder;
    }
}
