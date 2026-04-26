using CrestApps.Core.AI.Azure.AISearch.Services;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Azure.AISearch.Builders;
using CrestApps.Core.Azure.AISearch.Services;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Azure.AISearch;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core azure ai search ai document source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAzureAISearchAIDocumentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddKeyedScoped<IVectorSearchService>(AISearchConstants.ProviderName, (sp, _) => new AzureAISearchVectorSearchService(sp.GetRequiredService<IAzureAISearchClientFactory>().CreateSearchIndexClient(), sp.GetRequiredService<ILogger<AzureAISearchVectorSearchService>>()));

        return services.AddCoreAzureAISearchSource(IndexProfileTypes.AIDocuments, descriptor =>
                {
                    descriptor.DisplayName = "AI Documents";
                    descriptor.Description = "Create an Azure AI Search index for uploaded and embedded AI document chunks.";
                }).AddCoreAIDocumentIndexProfileHandler();
    }

    /// <summary>
    /// Adds core azure ai search ai data source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAzureAISearchAIDataSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCoreAzureAISearchSource(IndexProfileTypes.DataSource, descriptor =>
                {
                    descriptor.DisplayName = "Data Source";
                    descriptor.Description = "Create an Azure AI Search index for AI knowledge base data source documents.";
                }).AddCoreAIDataSourceRag().AddCoreAIDataSourceIndexProfileHandler();
    }

    /// <summary>
    /// Adds core azure ai search ai memory source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAzureAISearchAIMemorySource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddKeyedScoped<IMemoryVectorSearchService>(AISearchConstants.ProviderName, (sp, _) => new AzureAISearchMemoryVectorSearchService(sp.GetRequiredService<IAzureAISearchClientFactory>().CreateSearchIndexClient(), sp.GetRequiredService<ILogger<AzureAISearchMemoryVectorSearchService>>()));

        return services.AddCoreAzureAISearchSource(IndexProfileTypes.AIMemory, descriptor =>
                {
                    descriptor.DisplayName = "AI Memory";
                    descriptor.Description = "Create an Azure AI Search index for user and system memory records.";
                }).AddCoreAIMemoryIndexProfileHandler();
    }

    /// <summary>
    /// Adds core azure ai search source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="type">The type.</param>
    /// <param name="configure">The configure.</param>
    public static IServiceCollection AddCoreAzureAISearchSource(this IServiceCollection services, string type, Action<IndexProfileSourceDescriptor> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(type);

        services.AddCoreAIDefaultIndexProfileHandler();
        services.Configure<IndexProfileSourceOptions>(options => options.AddOrUpdate(AISearchConstants.ProviderName, "Azure AI Search", type, configure));

        return services;
    }

    /// <summary>
    /// Adds ai documents.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsAzureAISearchBuilder AddAIDocuments(this CrestAppsAzureAISearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAzureAISearchAIDocumentSource();

        return builder;
    }

    /// <summary>
    /// Adds ai data sources.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsAzureAISearchBuilder AddAIDataSources(this CrestAppsAzureAISearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAzureAISearchAIDataSource();

        return builder;
    }

    /// <summary>
    /// Adds ai memory.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsAzureAISearchBuilder AddAIMemory(this CrestAppsAzureAISearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAzureAISearchAIMemorySource();

        return builder;
    }
}
