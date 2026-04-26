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
    public static IServiceCollection AddCoreAzureAISearchServices(this IServiceCollection services, IConfigurationSection configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<AzureAISearchConnectionOptions>(configuration);

        return services.AddCoreAzureAISearchServices();
    }

    public static IServiceCollection AddCoreAzureAISearchServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddSingleton<IAzureAISearchClientFactory, AzureAISearchClientFactory>();
        services.TryAddSingleton(sp => sp.GetRequiredService<IAzureAISearchClientFactory>().CreateSearchIndexClient());

        services.TryAddKeyedScoped<IDataSourceContentManager>(AISearchConstants.ProviderName, (sp, _)
            => new AzureAISearchDataSourceContentManager(sp.GetRequiredService<SearchIndexClient>(), sp.GetRequiredService<ILogger<AzureAISearchDataSourceContentManager>>()));

        services.TryAddKeyedScoped<IDataSourceDocumentReader>(AISearchConstants.ProviderName, (sp, _)
            => new DataSourceAzureAISearchDocumentReader(sp.GetRequiredService<SearchIndexClient>()));

        services.TryAddKeyedSingleton<IODataFilterTranslator>(AISearchConstants.ProviderName, (_, _)
            => new AzureAIODataFilterTranslator());

        services.TryAddKeyedScoped<ISearchIndexManager>(AISearchConstants.ProviderName, (sp, _)
            => new AzureAISearchIndexManager(sp.GetRequiredService<SearchIndexClient>(), sp.GetRequiredService<IOptions<AzureAISearchConnectionOptions>>(), sp.GetRequiredService<ILogger<AzureAISearchIndexManager>>()));

        services.TryAddKeyedScoped<ISearchDocumentManager>(AISearchConstants.ProviderName, (sp, _)
            => new AzureAISearchDocumentManager(sp.GetRequiredService<SearchIndexClient>(), sp.GetServices<ISearchDocumentHandler>(), sp.GetRequiredService<ILogger<AzureAISearchDocumentManager>>()));

        return services;
    }

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
