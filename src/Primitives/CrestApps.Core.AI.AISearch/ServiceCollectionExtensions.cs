using Azure.Search.Documents.Indexes;
using CrestApps.Core.AI.AISearch.Services;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Azure.AISearch.Builders;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.AISearch;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreAzureAISearchAIDocumentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddKeyedScoped<IVectorSearchService>(AISearchConstants.ProviderName, (sp, _) => new AzureAISearchVectorSearchService(sp.GetRequiredService<SearchIndexClient>(), sp.GetRequiredService<ILogger<AzureAISearchVectorSearchService>>()));

        return services.AddCoreAzureAISearchSource(IndexProfileTypes.AIDocuments, descriptor =>
        {
            descriptor.DisplayName = "AI Documents";
            descriptor.Description = "Create an Azure AI Search index for uploaded and embedded AI document chunks.";
        }).AddCoreAIDocumentIndexProfileHandler();
    }

    public static IServiceCollection AddCoreAzureAISearchAIDataSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCoreAzureAISearchSource(IndexProfileTypes.DataSource, descriptor =>
        {
            descriptor.DisplayName = "Data Source";
            descriptor.Description = "Create an Azure AI Search index for AI knowledge base data source documents.";
        }).AddCoreAIDataSourceRag().AddCoreAIDataSourceIndexProfileHandler();
    }

    public static IServiceCollection AddCoreAzureAISearchAIMemorySource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddKeyedScoped<IMemoryVectorSearchService>(AISearchConstants.ProviderName, (sp, _) => new AzureAISearchMemoryVectorSearchService(sp.GetRequiredService<SearchIndexClient>(), sp.GetRequiredService<ILogger<AzureAISearchMemoryVectorSearchService>>()));

        return services.AddCoreAzureAISearchSource(IndexProfileTypes.AIMemory, descriptor =>
        {
            descriptor.DisplayName = "AI Memory";
            descriptor.Description = "Create an Azure AI Search index for user and system memory records.";
        }).AddCoreAIMemoryIndexProfileHandler();
    }

    public static IServiceCollection AddCoreAzureAISearchSource(this IServiceCollection services, string type, Action<IndexProfileSourceDescriptor> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(type);

        services.AddCoreAIDefaultIndexProfileHandler();
        services.Configure<IndexProfileSourceOptions>(options => options.AddOrUpdate(AISearchConstants.ProviderName, "Azure AI Search", type, configure));
        return services;
    }

    public static CrestAppsAzureAISearchBuilder AddAIDocuments(this CrestAppsAzureAISearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAzureAISearchAIDocumentSource();

        return builder;
    }

    public static CrestAppsAzureAISearchBuilder AddAIDataSources(this CrestAppsAzureAISearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAzureAISearchAIDataSource();

        return builder;
    }

    public static CrestAppsAzureAISearchBuilder AddAIMemory(this CrestAppsAzureAISearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAzureAISearchAIMemorySource();

        return builder;
    }
}
