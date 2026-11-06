using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Elasticsearch.Services;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Elasticsearch.Builders;
using CrestApps.Core.Elasticsearch.Services;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Elasticsearch;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core elasticsearch ai document source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreElasticsearchAIDocumentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddElasticsearchVectorSearchService(DefaultElasticsearchClient);

        return services.AddCoreElasticsearchSource(IndexProfileTypes.AIDocuments, descriptor =>
                {
                    descriptor.DisplayName = "AI Documents";
                    descriptor.Description = "Create an Elasticsearch index for uploaded and embedded AI document chunks.";
                }).AddCoreAIDocumentIndexProfileHandler();
    }

    /// <summary>
    /// Adds core elasticsearch ai data source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreElasticsearchAIDataSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCoreElasticsearchSource(IndexProfileTypes.DataSource, descriptor =>
                {
                    descriptor.DisplayName = "Data Source";
                    descriptor.Description = "Create an Elasticsearch index for AI knowledge base data source documents.";
                }).AddCoreAIDataSourceRag()
                .Configure<AIDataSourceSourceOptions>(options => options.AddOrUpdate(
                    AIDataSourceSourceTypes.Elasticsearch,
                    new LocalizedString("Elasticsearch", "Elasticsearch"),
                    new LocalizedString("Elasticsearch Source Description", "Read source documents from an external Elasticsearch index using explicit connection settings.")))
                .AddElasticsearchAIDataSourceSourceHandler(static sp => sp.GetRequiredService<IElasticsearchClientFactory>())
                .AddCoreAIDataSourceIndexProfileHandler();
    }

    /// <summary>
    /// Adds core elasticsearch ai memory source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreElasticsearchAIMemorySource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddElasticsearchMemoryVectorSearchService(DefaultElasticsearchClient);

        return services.AddCoreElasticsearchSource(IndexProfileTypes.AIMemory, descriptor =>
                {
                    descriptor.DisplayName = "AI Memory";
                    descriptor.Description = "Create an Elasticsearch index for user and system memory records.";
                }).AddCoreAIMemoryIndexProfileHandler();
    }

    /// <summary>
    /// Registers the Elasticsearch <see cref="IVectorSearchService"/>, keyed by
    /// <see cref="ElasticsearchConstants.ProviderName"/>, which searches the chunks of an AI documents index through
    /// the client that <paramref name="clientFactory"/> returns.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the client the service uses. It runs each time the scoped service is created, so return a client
    /// the host already holds, such as the client of the current tenant, rather than building a new one.
    /// </param>
    /// <param name="columnNames">
    /// The field names of the index, or <see langword="null"/> for <see cref="DocumentIndexColumnNames.Default"/>,
    /// the names of <see cref="DocumentIndexConstants.ColumnNames"/>.
    /// </param>
    /// <remarks>
    /// Registers only the search service: no index profile source and no index profile handler. The service is
    /// added only when none is registered for the key yet. <see cref="AddCoreElasticsearchAIDocumentSource"/>
    /// calls this method with the client of <see cref="IElasticsearchClientFactory"/>.
    /// </remarks>
    public static IServiceCollection AddElasticsearchVectorSearchService(this IServiceCollection services, Func<IServiceProvider, ElasticsearchClient> clientFactory, DocumentIndexColumnNames columnNames = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.TryAddKeyedScoped<IVectorSearchService>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchVectorSearchService(clientFactory(sp), columnNames, sp.GetRequiredService<ILogger<ElasticsearchVectorSearchService>>()));

        return services;
    }

    /// <summary>
    /// Registers the Elasticsearch <see cref="IMemoryVectorSearchService"/>, keyed by
    /// <see cref="ElasticsearchConstants.ProviderName"/>, which searches an AI memory index through the client that
    /// <paramref name="clientFactory"/> returns.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the client the service uses. It runs each time the scoped service is created, so return a client
    /// the host already holds, such as the client of the current tenant, rather than building a new one.
    /// </param>
    /// <remarks>
    /// Registers only the search service: no index profile source and no index profile handler. The service is
    /// added only when none is registered for the key yet. <see cref="AddCoreElasticsearchAIMemorySource"/>
    /// calls this method with the client of <see cref="IElasticsearchClientFactory"/>.
    /// </remarks>
    public static IServiceCollection AddElasticsearchMemoryVectorSearchService(this IServiceCollection services, Func<IServiceProvider, ElasticsearchClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.TryAddKeyedScoped<IMemoryVectorSearchService>(ElasticsearchConstants.ProviderName, (sp, _)
            => new ElasticsearchMemoryVectorSearchService(clientFactory(sp), sp.GetRequiredService<ILogger<ElasticsearchMemoryVectorSearchService>>()));

        return services;
    }

    /// <summary>
    /// Registers the <see cref="IAIDataSourceSourceHandler"/> that reads a data source from an external
    /// Elasticsearch index, keyed by <see cref="AIDataSourceSourceTypes.Elasticsearch"/>, with the default
    /// <see cref="ElasticsearchClientFactory"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// The handler connects with the connection settings stored on each data source, not with the host's
    /// own client, so the default factory needs no configuration. An <see cref="IElasticsearchClientFactory"/>
    /// the host already registered is used instead.
    /// </remarks>
    public static IServiceCollection AddElasticsearchAIDataSourceSourceHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddSingleton<IElasticsearchClientFactory, ElasticsearchClientFactory>();

        return services.AddElasticsearchAIDataSourceSourceHandler(static sp => sp.GetRequiredService<IElasticsearchClientFactory>());
    }

    /// <summary>
    /// Registers the <see cref="IAIDataSourceSourceHandler"/> that reads a data source from an external
    /// Elasticsearch index, keyed by <see cref="AIDataSourceSourceTypes.Elasticsearch"/>, creating its clients through
    /// the factory that <paramref name="clientFactory"/> returns.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the factory the handler uses. The handler calls
    /// <see cref="IElasticsearchClientFactory.Create(ElasticsearchConnectionOptions)"/> with the
    /// endpoint and credentials stored on each data source.
    /// </param>
    /// <remarks>
    /// The handler is added with <c>AddKeyedScoped</c>, so the last registration for the key wins.
    /// </remarks>
    public static IServiceCollection AddElasticsearchAIDataSourceSourceHandler(this IServiceCollection services, Func<IServiceProvider, IElasticsearchClientFactory> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.AddKeyedScoped<IAIDataSourceSourceHandler>(AIDataSourceSourceTypes.Elasticsearch, (sp, _)
            => new ElasticsearchAIDataSourceSourceHandler(
                clientFactory(sp),
                sp.GetRequiredService<IDataProtectionProvider>(),
                sp.GetRequiredService<ILogger<ElasticsearchAIDataSourceSourceHandler>>()));

        return services;
    }

    /// <summary>
    /// Adds core elasticsearch source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="type">The type.</param>
    /// <param name="configure">The configure.</param>
    public static IServiceCollection AddCoreElasticsearchSource(this IServiceCollection services, string type, Action<IndexProfileSourceDescriptor> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(type);

        services.AddCoreAIDefaultIndexProfileHandler();
        services.Configure<IndexProfileSourceOptions>(options => options
            .AddOrUpdate(ElasticsearchConstants.ProviderName, new LocalizedString("Elasticsearch", "Elasticsearch"), type, configure)
        );

        return services;
    }

    /// <summary>
    /// Adds ai documents.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsElasticsearchBuilder AddAIDocuments(this CrestAppsElasticsearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreElasticsearchAIDocumentSource();

        return builder;
    }

    /// <summary>
    /// Adds ai data sources.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsElasticsearchBuilder AddAIDataSources(this CrestAppsElasticsearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreElasticsearchAIDataSource();

        return builder;
    }

    /// <summary>
    /// Adds ai memory.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsElasticsearchBuilder AddAIMemory(this CrestAppsElasticsearchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreElasticsearchAIMemorySource();

        return builder;
    }

    private static ElasticsearchClient DefaultElasticsearchClient(IServiceProvider services)
        => services.GetRequiredService<IElasticsearchClientFactory>().Create();
}
