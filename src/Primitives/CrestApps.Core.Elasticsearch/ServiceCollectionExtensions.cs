using CrestApps.Core.AI;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Memory;
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
    public const string ProviderName = "Elasticsearch";
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
        services.TryAddKeyedScoped<IDataSourceContentManager>(ProviderName, (sp, _) => new ElasticsearchDataSourceContentManager(sp.GetRequiredService<ElasticsearchClient>(), sp.GetRequiredService<ILogger<ElasticsearchDataSourceContentManager>>()));
        services.TryAddKeyedScoped<IDataSourceDocumentReader>(ProviderName, (sp, _) => new DataSourceElasticsearchDocumentReader(sp.GetRequiredService<ElasticsearchClient>()));
        services.TryAddKeyedSingleton<IODataFilterTranslator>(ProviderName, (_, _) => new ElasticsearchODataFilterTranslator());
        services.TryAddKeyedScoped<ISearchIndexManager>(ProviderName, (sp, _) => new ElasticsearchSearchIndexManager(sp.GetRequiredService<ElasticsearchClient>(), sp.GetRequiredService<IOptions<ElasticsearchConnectionOptions>>(), sp.GetRequiredService<ILogger<ElasticsearchSearchIndexManager>>()));
        services.TryAddKeyedScoped<ISearchDocumentManager>(ProviderName, (sp, _) => new ElasticsearchSearchDocumentManager(sp.GetRequiredService<ElasticsearchClient>(), sp.GetRequiredService<ILogger<ElasticsearchSearchDocumentManager>>()));
        services.TryAddKeyedScoped<IVectorSearchService>(ProviderName, (sp, _) => new ElasticsearchVectorSearchService(sp.GetRequiredService<ElasticsearchClient>(), sp.GetRequiredService<ILogger<ElasticsearchVectorSearchService>>()));
        services.TryAddKeyedScoped<IMemoryVectorSearchService>(ProviderName, (sp, _) => new ElasticsearchMemoryVectorSearchService(sp.GetRequiredService<ElasticsearchClient>(), sp.GetRequiredService<ILogger<ElasticsearchMemoryVectorSearchService>>()));
        return services;
    }

    public static IServiceCollection AddCoreElasticsearchSource(this IServiceCollection services, string type, Action<IndexProfileSourceDescriptor> configure = null)
    {
        services.AddCoreAIDefaultIndexProfileHandler();
        services.Configure<IndexProfileSourceOptions>(options => options.AddOrUpdate(ProviderName, "Elasticsearch", type, configure));
        return services;
    }

    public static IServiceCollection AddCoreElasticsearchAIDocumentSource(this IServiceCollection services)
    {
        return services.AddCoreElasticsearchSource(IndexProfileTypes.AIDocuments, descriptor =>
        {
            descriptor.DisplayName = "AI Documents";
            descriptor.Description = "Create an Elasticsearch index for uploaded and embedded AI document chunks.";
        }).AddCoreAIDocumentIndexProfileHandler();
    }

    public static IServiceCollection AddCoreElasticsearchAIDataSource(this IServiceCollection services)
    {
        return services.AddCoreElasticsearchSource(IndexProfileTypes.DataSource, descriptor =>
        {
            descriptor.DisplayName = "Data Source";
            descriptor.Description = "Create an Elasticsearch index for AI knowledge base data source documents.";
        }).AddCoreAIDataSourceRag().AddCoreAIDataSourceIndexProfileHandler();
    }

    public static IServiceCollection AddCoreElasticsearchAIMemorySource(this IServiceCollection services)
    {
        return services.AddCoreElasticsearchSource(IndexProfileTypes.AIMemory, descriptor =>
        {
            descriptor.DisplayName = "AI Memory";
            descriptor.Description = "Create an Elasticsearch index for user and system memory records.";
        }).AddCoreAIMemoryIndexProfileHandler();
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

    [Obsolete("Use AddIndexingServices(indexing => indexing.AddElasticsearch(...)).")]
    public static CrestAppsCoreBuilder AddElasticsearch(this CrestAppsCoreBuilder builder, IConfigurationSection configuration, Action<CrestAppsElasticsearchBuilder> configure = null)
    {
        builder.Services.AddCoreElasticsearchServices(configuration);

        if (configure is not null)
        {
            configure(new CrestAppsElasticsearchBuilder(builder.Services));
        }

        return builder;
    }

    [Obsolete("Use AddIndexingServices(indexing => indexing.AddElasticsearch(...)).")]
    public static CrestAppsCoreBuilder AddElasticsearch(this CrestAppsCoreBuilder builder, Action<CrestAppsElasticsearchBuilder> configure = null)
    {
        builder.Services.AddCoreElasticsearchServices();

        if (configure is not null)
        {
            configure(new CrestAppsElasticsearchBuilder(builder.Services));
        }

        return builder;
    }

    public static CrestAppsElasticsearchBuilder AddAIDocuments(this CrestAppsElasticsearchBuilder builder)
    {
        builder.Services.AddCoreElasticsearchAIDocumentSource();
        return builder;
    }

    public static CrestAppsElasticsearchBuilder AddAIDataSources(this CrestAppsElasticsearchBuilder builder)
    {
        builder.Services.AddCoreElasticsearchAIDataSource();
        return builder;
    }

    public static CrestAppsElasticsearchBuilder AddAIMemory(this CrestAppsElasticsearchBuilder builder)
    {
        builder.Services.AddCoreElasticsearchAIMemorySource();
        return builder;
    }

}
