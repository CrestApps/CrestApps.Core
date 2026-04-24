using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Elasticsearch.Services;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Elasticsearch.Builders;
using CrestApps.Core.Elasticsearch.Services;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Elasticsearch;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreElasticsearchAIDocumentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddKeyedScoped<IVectorSearchService>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchVectorSearchService(sp.GetRequiredService<IElasticsearchClientFactory>().Create(), sp.GetRequiredService<ILogger<ElasticsearchVectorSearchService>>()));

        return services.AddCoreElasticsearchSource(IndexProfileTypes.AIDocuments, descriptor =>
        {
            descriptor.DisplayName = "AI Documents";
            descriptor.Description = "Create an Elasticsearch index for uploaded and embedded AI document chunks.";
        }).AddCoreAIDocumentIndexProfileHandler();
    }

    public static IServiceCollection AddCoreElasticsearchAIDataSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCoreElasticsearchSource(IndexProfileTypes.DataSource, descriptor =>
        {
            descriptor.DisplayName = "Data Source";
            descriptor.Description = "Create an Elasticsearch index for AI knowledge base data source documents.";
        }).AddCoreAIDataSourceRag()
        .AddCoreAIDataSourceIndexProfileHandler();
    }

    public static IServiceCollection AddCoreElasticsearchAIMemorySource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddKeyedScoped<IMemoryVectorSearchService>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchMemoryVectorSearchService(sp.GetRequiredService<IElasticsearchClientFactory>().Create(), sp.GetRequiredService<ILogger<ElasticsearchMemoryVectorSearchService>>()));

        return services.AddCoreElasticsearchSource(IndexProfileTypes.AIMemory, descriptor =>
        {
            descriptor.DisplayName = "AI Memory";
            descriptor.Description = "Create an Elasticsearch index for user and system memory records.";
        }).AddCoreAIMemoryIndexProfileHandler();
    }

    public static IServiceCollection AddCoreElasticsearchSource(this IServiceCollection services, string type, Action<IndexProfileSourceDescriptor> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(type);

        services.AddCoreAIDefaultIndexProfileHandler();
        services.Configure<IndexProfileSourceOptions>(options => options
            .AddOrUpdate(ElasticsearchConstants.ProviderName, "Elasticsearch", type, configure)
        );

        return services;
    }

    public static CrestAppsElasticsearchBuilder AddAIDocuments(this CrestAppsElasticsearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreElasticsearchAIDocumentSource();

        return builder;
    }

    public static CrestAppsElasticsearchBuilder AddAIDataSources(this CrestAppsElasticsearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreElasticsearchAIDataSource();

        return builder;
    }

    public static CrestAppsElasticsearchBuilder AddAIMemory(this CrestAppsElasticsearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreElasticsearchAIMemorySource();

        return builder;
    }
}
