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

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreElasticsearchServices(this IServiceCollection services, IConfigurationSection configuration)
    {
        services.Configure<ElasticsearchConnectionOptions>(configuration);
        var options = new ElasticsearchConnectionOptions();
        configuration.Bind(options);
        if (!string.IsNullOrEmpty(options.Url))
        {
            var settings = new ElasticsearchClientSettings(new Uri(options.Url));
            if (!string.IsNullOrEmpty(options.Username) && !string.IsNullOrEmpty(options.Password))
            {
                settings.Authentication(new Elastic.Transport.BasicAuthentication(options.Username, options.Password));
            }

            if (!string.IsNullOrEmpty(options.CertificateFingerprint))
            {
                settings.CertificateFingerprint(options.CertificateFingerprint);
            }

            services.TryAddSingleton(new ElasticsearchClient(settings));
        }

        return services.AddCoreElasticsearchServices();
    }

    public static IServiceCollection AddCoreElasticsearchServices(this IServiceCollection services)
    {
        services.TryAddKeyedScoped<IDataSourceContentManager>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchDataSourceContentManager(sp.GetRequiredService<ElasticsearchClient>(), sp.GetRequiredService<ILogger<ElasticsearchDataSourceContentManager>>()));

        services.TryAddKeyedScoped<IDataSourceDocumentReader>(ElasticsearchConstants.ProviderName, (sp, _)
            => new DataSourceElasticsearchDocumentReader(sp.GetRequiredService<ElasticsearchClient>()));

        services.TryAddKeyedSingleton<IODataFilterTranslator>(ElasticsearchConstants.ProviderName, (_, _)
            => new ElasticsearchODataFilterTranslator());

        services.TryAddKeyedScoped<ISearchIndexManager>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchSearchIndexManager(sp.GetRequiredService<ElasticsearchClient>(), sp.GetRequiredService<IOptions<ElasticsearchConnectionOptions>>(), sp.GetRequiredService<ILogger<ElasticsearchSearchIndexManager>>()));

        services.TryAddKeyedScoped<ISearchDocumentManager>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchSearchDocumentManager(sp.GetRequiredService<ElasticsearchClient>(), sp.GetRequiredService<ILogger<ElasticsearchSearchDocumentManager>>()));

        return services;
    }

    public static CrestAppsIndexingBuilder AddElasticsearch(this CrestAppsIndexingBuilder builder, IConfigurationSection configuration, Action<CrestAppsElasticsearchBuilder> configure = null)
    {
        builder.Services.AddCoreElasticsearchServices(configuration);

        if (configure is not null)
        {
            configure(new CrestAppsElasticsearchBuilder(builder.Services));
        }

        return builder;
    }

    public static CrestAppsIndexingBuilder AddElasticsearch(this CrestAppsIndexingBuilder builder, Action<CrestAppsElasticsearchBuilder> configure = null)
    {
        builder.Services.AddCoreElasticsearchServices();

        if (configure is not null)
        {
            configure(new CrestAppsElasticsearchBuilder(builder.Services));
        }

        return builder;
    }
}
