using Azure.Search.Documents.Indexes;
using CrestApps.Core.AI.Azure.AISearch.Services;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Azure.AISearch.Builders;
using CrestApps.Core.Azure.AISearch.Services;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
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

        services.AddAzureAISearchVectorSearchService(DefaultSearchIndexClient);

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
                }).AddCoreAIDataSourceRag()
                .Configure<AIDataSourceSourceOptions>(options => options.AddOrUpdate(
                    AIDataSourceSourceTypes.AzureAISearch,
                    new LocalizedString("Azure AI Search", "Azure AI Search"),
                    new LocalizedString("Azure AI Search Source Description", "Read source documents from an external Azure AI Search index using explicit connection settings.")))
                .AddAzureAISearchAIDataSourceSourceHandler(static sp => sp.GetRequiredService<IAzureAISearchClientFactory>())
                .AddCoreAIDataSourceIndexProfileHandler();
    }

    /// <summary>
    /// Adds core azure ai search ai memory source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAzureAISearchAIMemorySource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAzureAISearchMemoryVectorSearchService(DefaultSearchIndexClient);

        return services.AddCoreAzureAISearchSource(IndexProfileTypes.AIMemory, descriptor =>
                {
                    descriptor.DisplayName = "AI Memory";
                    descriptor.Description = "Create an Azure AI Search index for user and system memory records.";
                }).AddCoreAIMemoryIndexProfileHandler();
    }

    /// <summary>
    /// Registers the Azure AI Search <see cref="IVectorSearchService"/>, keyed by
    /// <see cref="AISearchConstants.ProviderName"/>, which searches the chunks of an AI documents index through
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
    /// added only when none is registered for the key yet. <see cref="AddCoreAzureAISearchAIDocumentSource"/>
    /// calls this method with the client of <see cref="IAzureAISearchClientFactory"/>.
    /// </remarks>
    public static IServiceCollection AddAzureAISearchVectorSearchService(this IServiceCollection services, Func<IServiceProvider, SearchIndexClient> clientFactory, DocumentIndexColumnNames columnNames = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.TryAddKeyedScoped<IVectorSearchService>(AISearchConstants.ProviderName, (sp, _)
            => new AzureAISearchVectorSearchService(clientFactory(sp), columnNames, sp.GetRequiredService<ILogger<AzureAISearchVectorSearchService>>()));

        return services;
    }

    /// <summary>
    /// Registers the Azure AI Search <see cref="IMemoryVectorSearchService"/>, keyed by
    /// <see cref="AISearchConstants.ProviderName"/>, which searches an AI memory index through the client that
    /// <paramref name="clientFactory"/> returns.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the client the service uses. It runs each time the scoped service is created, so return a client
    /// the host already holds, such as the client of the current tenant, rather than building a new one.
    /// </param>
    /// <remarks>
    /// Registers only the search service: no index profile source and no index profile handler. The service is
    /// added only when none is registered for the key yet. <see cref="AddCoreAzureAISearchAIMemorySource"/>
    /// calls this method with the client of <see cref="IAzureAISearchClientFactory"/>.
    /// </remarks>
    public static IServiceCollection AddAzureAISearchMemoryVectorSearchService(this IServiceCollection services, Func<IServiceProvider, SearchIndexClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.TryAddKeyedScoped<IMemoryVectorSearchService>(AISearchConstants.ProviderName, (sp, _)
            => new AzureAISearchMemoryVectorSearchService(clientFactory(sp), sp.GetRequiredService<ILogger<AzureAISearchMemoryVectorSearchService>>()));

        return services;
    }

    /// <summary>
    /// Registers the <see cref="IAIDataSourceSourceHandler"/> that reads a data source from an external Azure AI
    /// Search index, keyed by <see cref="AIDataSourceSourceTypes.AzureAISearch"/>, with the default
    /// <see cref="AzureAISearchClientFactory"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// The handler connects with the endpoint and credentials stored on each data source, not with the host's
    /// own client, so the default factory needs no configuration. An <see cref="IAzureAISearchClientFactory"/>
    /// the host already registered is used instead.
    /// </remarks>
    public static IServiceCollection AddAzureAISearchAIDataSourceSourceHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddSingleton<IAzureAISearchClientFactory, AzureAISearchClientFactory>();

        return services.AddAzureAISearchAIDataSourceSourceHandler(static sp => sp.GetRequiredService<IAzureAISearchClientFactory>());
    }

    /// <summary>
    /// Registers the <see cref="IAIDataSourceSourceHandler"/> that reads a data source from an external Azure AI
    /// Search index, keyed by <see cref="AIDataSourceSourceTypes.AzureAISearch"/>, creating its clients through
    /// the factory that <paramref name="clientFactory"/> returns.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientFactory">
    /// Returns the factory the handler uses. The handler calls
    /// <see cref="IAzureAISearchClientFactory.CreateSearchClient(string, AzureAISearchConnectionOptions)"/> with the
    /// endpoint and credentials stored on each data source.
    /// </param>
    /// <remarks>
    /// The handler is added with <c>AddKeyedScoped</c>, so the last registration for the key wins.
    /// </remarks>
    public static IServiceCollection AddAzureAISearchAIDataSourceSourceHandler(this IServiceCollection services, Func<IServiceProvider, IAzureAISearchClientFactory> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clientFactory);

        services.AddKeyedScoped<IAIDataSourceSourceHandler>(AIDataSourceSourceTypes.AzureAISearch, (sp, _)
            => new AzureAISearchAIDataSourceSourceHandler(
                clientFactory(sp),
                sp.GetRequiredService<IDataProtectionProvider>(),
                sp.GetRequiredService<ILogger<AzureAISearchAIDataSourceSourceHandler>>()));

        return services;
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
        services.Configure<IndexProfileSourceOptions>(options => options.AddOrUpdate(
            AISearchConstants.ProviderName,
            new LocalizedString("Azure AI Search", "Azure AI Search"),
            type,
            configure));

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

    private static SearchIndexClient DefaultSearchIndexClient(IServiceProvider services)
        => services.GetRequiredService<IAzureAISearchClientFactory>().CreateSearchIndexClient();
}
