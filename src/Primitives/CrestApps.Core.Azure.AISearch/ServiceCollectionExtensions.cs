using Azure;
using Azure.Identity;
using Azure.Search.Documents.Indexes;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Memory;
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
public static class ServiceCollectionExtensions
{
    public const string ProviderName = "AzureAISearch";
    public static IServiceCollection AddCoreAzureAISearchServices(this IServiceCollection services, IConfigurationSection configuration)
    {
        services.Configure<AzureAISearchConnectionOptions>(configuration);
        var options = new AzureAISearchConnectionOptions();
        configuration.Bind(options);
        if (!string.IsNullOrEmpty(options.Endpoint))
        {
            var endpoint = new Uri(options.Endpoint);
            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                services.TryAddSingleton(new SearchIndexClient(endpoint, new AzureKeyCredential(options.ApiKey)));
            }
            else
            {
                services.TryAddSingleton(new SearchIndexClient(endpoint, new DefaultAzureCredential()));
            }
        }

        return services.AddCoreAzureAISearchServices();
    }

    public static IServiceCollection AddCoreAzureAISearchServices(this IServiceCollection services)
    {
        services.TryAddKeyedScoped<IDataSourceContentManager>(ProviderName, (sp, _) => new AzureAISearchDataSourceContentManager(sp.GetRequiredService<SearchIndexClient>(), sp.GetRequiredService<ILogger<AzureAISearchDataSourceContentManager>>()));
        services.TryAddKeyedScoped<IDataSourceDocumentReader>(ProviderName, (sp, _) => new DataSourceAzureAISearchDocumentReader(sp.GetRequiredService<SearchIndexClient>()));
        services.TryAddKeyedSingleton<IODataFilterTranslator>(ProviderName, (_, _) => new AzureAIODataFilterTranslator());
        services.TryAddKeyedScoped<ISearchIndexManager>(ProviderName, (sp, _) => new AzureAISearchIndexManager(sp.GetRequiredService<SearchIndexClient>(), sp.GetRequiredService<IOptions<AzureAISearchConnectionOptions>>(), sp.GetRequiredService<ILogger<AzureAISearchIndexManager>>()));
        services.TryAddKeyedScoped<ISearchDocumentManager>(ProviderName, (sp, _) => new AzureAISearchDocumentManager(sp.GetRequiredService<SearchIndexClient>(), sp.GetRequiredService<ILogger<AzureAISearchDocumentManager>>()));
        services.TryAddKeyedScoped<IVectorSearchService>(ProviderName, (sp, _) => new AzureAISearchVectorSearchService(sp.GetRequiredService<SearchIndexClient>(), sp.GetRequiredService<ILogger<AzureAISearchVectorSearchService>>()));
        services.TryAddKeyedScoped<IMemoryVectorSearchService>(ProviderName, (sp, _) => new AzureAISearchMemoryVectorSearchService(sp.GetRequiredService<SearchIndexClient>(), sp.GetRequiredService<ILogger<AzureAISearchMemoryVectorSearchService>>()));
        return services;
    }

    public static IServiceCollection AddCoreAzureAISearchSource(this IServiceCollection services, string type, Action<IndexProfileSourceDescriptor> configure = null)
    {
        services.AddCoreAIDefaultIndexProfileHandler();
        services.Configure<IndexProfileSourceOptions>(options => options.AddOrUpdate(ProviderName, "Azure AI Search", type, configure));
        return services;
    }

    public static IServiceCollection AddCoreAzureAISearchAIDocumentSource(this IServiceCollection services)
    {
        return services.AddCoreAzureAISearchSource(IndexProfileTypes.AIDocuments, descriptor =>
        {
            descriptor.DisplayName = "AI Documents";
            descriptor.Description = "Create an Azure AI Search index for uploaded and embedded AI document chunks.";
        }).AddCoreAIDocumentIndexProfileHandler();
    }

    public static IServiceCollection AddCoreAzureAISearchAIDataSource(this IServiceCollection services)
    {
        return services.AddCoreAzureAISearchSource(IndexProfileTypes.DataSource, descriptor =>
        {
            descriptor.DisplayName = "Data Source";
            descriptor.Description = "Create an Azure AI Search index for AI knowledge base data source documents.";
        }).AddCoreAIDataSourceRag().AddCoreAIDataSourceIndexProfileHandler();
    }

    public static IServiceCollection AddCoreAzureAISearchAIMemorySource(this IServiceCollection services)
    {
        return services.AddCoreAzureAISearchSource(IndexProfileTypes.AIMemory, descriptor =>
        {
            descriptor.DisplayName = "AI Memory";
            descriptor.Description = "Create an Azure AI Search index for user and system memory records.";
        }).AddCoreAIMemoryIndexProfileHandler();
    }

    public static CrestAppsIndexingBuilder AddAzureAISearch(this CrestAppsIndexingBuilder builder, IConfigurationSection configuration, Action<CrestAppsAzureAISearchBuilder> configure = null)
    {
        builder.Services.AddCoreAzureAISearchServices(configuration);

        if (configure is not null)
        {
            configure(new CrestAppsAzureAISearchBuilder(builder.Services));
        }

        return builder;
    }

    public static CrestAppsIndexingBuilder AddAzureAISearch(this CrestAppsIndexingBuilder builder, Action<CrestAppsAzureAISearchBuilder> configure = null)
    {
        builder.Services.AddCoreAzureAISearchServices();

        if (configure is not null)
        {
            configure(new CrestAppsAzureAISearchBuilder(builder.Services));
        }

        return builder;
    }

    [Obsolete("Use AddIndexingServices(indexing => indexing.AddAzureAISearch(...)).")]
    public static CrestAppsCoreBuilder AddAzureAISearch(this CrestAppsCoreBuilder builder, IConfigurationSection configuration, Action<CrestAppsAzureAISearchBuilder> configure = null)
    {
        builder.Services.AddCoreAzureAISearchServices(configuration);

        if (configure is not null)
        {
            configure(new CrestAppsAzureAISearchBuilder(builder.Services));
        }

        return builder;
    }

    [Obsolete("Use AddIndexingServices(indexing => indexing.AddAzureAISearch(...)).")]
    public static CrestAppsCoreBuilder AddAzureAISearch(this CrestAppsCoreBuilder builder, Action<CrestAppsAzureAISearchBuilder> configure = null)
    {
        builder.Services.AddCoreAzureAISearchServices();

        if (configure is not null)
        {
            configure(new CrestAppsAzureAISearchBuilder(builder.Services));
        }

        return builder;
    }

    public static CrestAppsAzureAISearchBuilder AddAIDocuments(this CrestAppsAzureAISearchBuilder builder)
    {
        builder.Services.AddCoreAzureAISearchAIDocumentSource();
        return builder;
    }

    public static CrestAppsAzureAISearchBuilder AddAIDataSources(this CrestAppsAzureAISearchBuilder builder)
    {
        builder.Services.AddCoreAzureAISearchAIDataSource();
        return builder;
    }

    public static CrestAppsAzureAISearchBuilder AddAIMemory(this CrestAppsAzureAISearchBuilder builder)
    {
        builder.Services.AddCoreAzureAISearchAIMemorySource();
        return builder;
    }

}
