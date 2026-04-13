using CrestApps.Core.AI;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Builders;
using CrestApps.Core.Data.YesSql.Indexes;
using CrestApps.Core.Data.YesSql.Indexes.A2A;
using CrestApps.Core.Data.YesSql.Indexes.AI;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using CrestApps.Core.Data.YesSql.Indexes.AIMemory;
using CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;
using CrestApps.Core.Data.YesSql.Indexes.DataSources;
using CrestApps.Core.Data.YesSql.Indexes.Indexing;
using CrestApps.Core.Data.YesSql.Indexes.Mcp;
using CrestApps.Core.Data.YesSql.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Indexes;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreYesSqlDataStore(this IServiceCollection services, Func<Configuration, IConfiguration> configure)
    {
        services.AddOptions<YesSqlStoreOptions>();

        services.AddSingleton(sp =>
        {
            var store = StoreFactory.CreateAndInitializeAsync(configure(new Configuration())).GetAwaiter().GetResult();

            store.RegisterIndexes(sp.GetServices<IIndexProvider>());

            return store;
        });

        services.AddScoped(sp => sp.GetRequiredService<IStore>().CreateSession());
        services.AddScoped<IStoreCommitter, YesSqlStoreCommitter>();
        services.AddCatalogManagers();

        return services;
    }

    public static CrestAppsCoreBuilder AddYesSqlDataStore(this CrestAppsCoreBuilder builder, Func<Configuration, IConfiguration> configure)
    {
        builder.Services.AddCoreYesSqlDataStore(configure);

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the core AI services feature on the AI suite builder.
    /// This includes catalogs for <see cref="AIProfile"/>, <see cref="AIProfileTemplate"/>,
    /// multi-source binding sources for <see cref="AIProviderConnection"/> and <see cref="AIDeployment"/>,
    /// and the chat session stores (<see cref="IAIChatSessionManager"/> and <see cref="IAIChatSessionPromptStore"/>).
    /// </summary>
    public static CrestAppsAISuiteBuilder AddYesSqlStores(this CrestAppsAISuiteBuilder builder)
    {
        builder.Services.AddCoreAIServicesStoresYesSql();
        builder.Services.AddCoreAIProfileTemplateStoresYesSql();
        builder.Services.AddCoreAIChatSessionStoresYesSql();

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the A2A client feature on the A2A client builder.
    /// This includes a catalog for <see cref="A2AConnection"/>.
    /// </summary>
    public static CrestAppsA2AClientBuilder AddYesSqlStores(this CrestAppsA2AClientBuilder builder)
    {
        builder.Services.AddCoreAIA2AClientStoresYesSql();

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the MCP client feature on the MCP client builder.
    /// This includes a catalog for <see cref="McpConnection"/>.
    /// </summary>
    public static CrestAppsMcpClientBuilder AddYesSqlStores(this CrestAppsMcpClientBuilder builder)
    {
        builder.Services.AddCoreAIMcpClientStoresYesSql();

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the MCP server feature on the MCP server builder.
    /// This includes catalogs for <see cref="McpPrompt"/> and <see cref="McpResource"/>.
    /// </summary>
    public static CrestAppsMcpServerBuilder AddYesSqlStores(this CrestAppsMcpServerBuilder builder)
    {
        builder.Services.AddCoreAIMcpServerStoresYesSql();

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the chat interactions feature on the chat interactions builder.
    /// This includes a catalog for <see cref="ChatInteraction"/> and <see cref="IChatInteractionPromptStore"/>.
    /// </summary>
    public static CrestAppsChatInteractionsBuilder AddYesSqlStores(this CrestAppsChatInteractionsBuilder builder)
    {
        builder.Services.AddCoreAIChatInteractionStoresYesSql();

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the document processing feature on the document processing builder.
    /// This includes <see cref="IAIDocumentStore"/>, <see cref="IAIDocumentChunkStore"/>,
    /// and <see cref="IAIDataSourceStore"/>.
    /// </summary>
    public static CrestAppsDocumentProcessingBuilder AddYesSqlStores(this CrestAppsDocumentProcessingBuilder builder)
    {
        builder.Services.AddCoreAIDocumentProcessingStoresYesSql();
        builder.Services.AddCoreAIDataSourceStoresYesSql();

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the AI memory feature on the AI memory builder.
    /// This includes <see cref="IAIMemoryStore"/>.
    /// </summary>
    public static CrestAppsAIMemoryBuilder AddYesSqlStores(this CrestAppsAIMemoryBuilder builder)
    {
        builder.Services.AddCoreAIMemoryStoresYesSql();

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the indexing services feature on the indexing builder.
    /// This includes <see cref="ISearchIndexProfileStore"/> and the <see cref="SearchIndexProfileIndexProvider"/>.
    /// </summary>
    public static CrestAppsIndexingBuilder AddYesSqlStores(this CrestAppsIndexingBuilder builder)
    {
        builder.Services.AddCoreIndexingStoresYesSql();

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the core AI services feature.
    /// This includes a catalog for <see cref="AIProfile"/>
    /// and multi-source binding sources for <see cref="AIProviderConnection"/> and <see cref="AIDeployment"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIServicesStoresYesSql(this IServiceCollection services)
    {
        AddYesSqlNamedSourceDocumentCatalog<AIProfile, AIProfileIndex>(services, static o => o.AICollectionName);
        AddYesSqlNamedSourceBindingSource<AIProviderConnection, AIProviderConnectionIndex>(services, static o => o.AICollectionName);
        AddYesSqlNamedSourceBindingSource<AIDeployment, AIDeploymentIndex>(services, static o => o.AICollectionName);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIProfileIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIProviderConnectionIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIDeploymentIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the AI profile template feature.
    /// This includes a catalog for <see cref="AIProfileTemplate"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIProfileTemplateStoresYesSql(this IServiceCollection services)
    {
        AddYesSqlNamedSourceDocumentCatalog<AIProfileTemplate, AIProfileTemplateIndex>(services, static o => o.AICollectionName);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIProfileTemplateIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the indexing services feature.
    /// This includes <see cref="ISearchIndexProfileStore"/> and the <see cref="SearchIndexProfileIndexProvider"/>.
    /// </summary>
    public static IServiceCollection AddCoreIndexingStoresYesSql(this IServiceCollection services)
    {
        services.TryAddScoped<ISearchIndexProfileStore, YesSqlSearchIndexProfileStore>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, SearchIndexProfileIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the A2A client feature.
    /// This includes a catalog for <see cref="A2AConnection"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIA2AClientStoresYesSql(this IServiceCollection services)
    {
        AddYesSqlDocumentCatalog<A2AConnection, A2AConnectionIndex>(services, static o => o.AICollectionName);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, A2AConnectionIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the MCP client feature.
    /// This includes a catalog for <see cref="McpConnection"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIMcpClientStoresYesSql(this IServiceCollection services)
    {
        AddYesSqlSourceDocumentCatalog<McpConnection, McpConnectionIndex>(services, static o => o.AICollectionName);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, McpConnectionIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the MCP server feature.
    /// This includes catalogs for <see cref="McpPrompt"/> and <see cref="McpResource"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIMcpServerStoresYesSql(this IServiceCollection services)
    {
        AddYesSqlNamedDocumentCatalog<McpPrompt, McpPromptIndex>(services, static o => o.AICollectionName);
        AddYesSqlSourceDocumentCatalog<McpResource, McpResourceIndex>(services, static o => o.AICollectionName);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, McpPromptIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, McpResourceIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers all YesSql-backed stores for the AI chat sessions feature.
    /// This is a convenience method that registers the core chat session stores
    /// (<see cref="IAIChatSessionManager"/> and <see cref="IAIChatSessionPromptStore"/>)
    /// plus the optional metrics, completion usage, and extracted data index providers.
    /// Implementations that need finer-grained control (e.g., Orchard Core) can call
    /// the individual extension methods instead.
    /// </summary>
    public static IServiceCollection AddCoreAIChatSessionStoresYesSql(this IServiceCollection services)
    {
        services.AddCoreAIChatSessionBaseStoresYesSql();
        services.AddCoreAIChatSessionMetricsStoresYesSql();
        services.AddCoreAICompletionUsageStoresYesSql();
        services.AddCoreAIChatSessionExtractedDataStoresYesSql();

        return services;
    }

    /// <summary>
    /// Registers the core YesSql-backed chat session stores:
    /// <see cref="IAIChatSessionManager"/> and <see cref="IAIChatSessionPromptStore"/>,
    /// along with the <see cref="AIChatSessionIndexProvider"/> and <see cref="AIChatSessionPromptIndexProvider"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIChatSessionBaseStoresYesSql(this IServiceCollection services)
    {
        services.AddScoped<IAIChatSessionManager, YesSqlAIChatSessionManager>();
        services.AddScoped<IAIChatSessionPromptStore, YesSqlAIChatSessionPromptStore>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIChatSessionIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIChatSessionPromptIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers the YesSql <see cref="AIChatSessionMetricsIndexProvider"/> for the chat session analytics feature.
    /// </summary>
    public static IServiceCollection AddCoreAIChatSessionMetricsStoresYesSql(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIChatSessionMetricsIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers the YesSql <see cref="AICompletionUsageIndexProvider"/> for the AI completion usage tracking feature.
    /// </summary>
    public static IServiceCollection AddCoreAICompletionUsageStoresYesSql(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AICompletionUsageIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers the YesSql <see cref="AIChatSessionExtractedDataIndexProvider"/> for the chat session extracted data feature.
    /// </summary>
    public static IServiceCollection AddCoreAIChatSessionExtractedDataStoresYesSql(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIChatSessionExtractedDataIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the document processing feature.
    /// This includes <see cref="IAIDocumentStore"/> and <see cref="IAIDocumentChunkStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIDocumentProcessingStoresYesSql(this IServiceCollection services)
    {
        services.AddScoped<IAIDocumentStore, YesSqlAIDocumentStore>();
        services.AddScoped<IAIDocumentChunkStore, YesSqlAIDocumentChunkStore>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIDocumentIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIDocumentChunkIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the data source RAG feature.
    /// This includes <see cref="IAIDataSourceStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIDataSourceStoresYesSql(this IServiceCollection services)
    {
        services.TryAddScoped<IAIDataSourceStore, YesSqlAIDataSourceStore>();
        services.AddScoped<ICatalog<AIDataSource>>(sp => sp.GetRequiredService<IAIDataSourceStore>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIDataSourceIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the AI memory feature.
    /// This includes <see cref="IAIMemoryStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIMemoryStoresYesSql(this IServiceCollection services)
    {
        services.AddScoped<IAIMemoryStore, YesSqlAIMemoryStore>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIMemoryEntryIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the chat interactions feature.
    /// This includes a catalog for <see cref="ChatInteraction"/>
    /// and <see cref="IChatInteractionPromptStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIChatInteractionStoresYesSql(this IServiceCollection services)
    {
        AddYesSqlDocumentCatalog<ChatInteraction, ChatInteractionIndex>(services, static o => o.AICollectionName);
        services.AddScoped<IChatInteractionPromptStore, YesSqlChatInteractionPromptStore>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, ChatInteractionIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, ChatInteractionPromptIndexProvider>());

        return services;
    }

    public static IServiceCollection AddYesSqlDocumentCatalogs(this IServiceCollection services)
    {
        services.TryAddScoped(typeof(ICatalog<>), typeof(DocumentCatalog<,>));
        services.TryAddScoped(typeof(INamedCatalog<>), typeof(NamedDocumentCatalog<,>));
        services.TryAddScoped(typeof(ISourceCatalog<>), typeof(SourceDocumentCatalog<,>));
        services.TryAddScoped(typeof(INamedSourceCatalog<>), typeof(NamedSourceDocumentCatalog<,>));

        return services;
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="DocumentCatalog{TModel,TIndex}"/> using
    /// the <see cref="YesSqlStoreOptions.DefaultCollectionName"/>.
    /// </summary>
    public static IServiceCollection AddYesSqlDocumentCatalog<TModel, TIndex>(this IServiceCollection services)
        where TModel : CatalogItem
        where TIndex : CatalogItemIndex
    {
        return AddYesSqlDocumentCatalog<TModel, TIndex>(services, static o => o.DefaultCollectionName);
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="DocumentCatalog{TModel,TIndex}"/> using
    /// the specified <paramref name="collection"/> name.
    /// </summary>
    public static IServiceCollection AddYesSqlDocumentCatalog<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem
        where TIndex : CatalogItemIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();

            return new DocumentCatalog<TModel, TIndex>(session, collection);
        });

        return services;
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="NamedDocumentCatalog{TModel,TIndex}"/> using
    /// the <see cref="YesSqlStoreOptions.DefaultCollectionName"/>.
    /// </summary>
    public static IServiceCollection AddYesSqlNamedDocumentCatalog<TModel, TIndex>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex
    {
        return AddYesSqlNamedDocumentCatalog<TModel, TIndex>(services, static o => o.DefaultCollectionName);
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="NamedDocumentCatalog{TModel,TIndex}"/> using
    /// the specified <paramref name="collection"/> name.
    /// </summary>
    public static IServiceCollection AddYesSqlNamedDocumentCatalog<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, INameAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();

        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();

            return new NamedDocumentCatalog<TModel, TIndex>(session, collection);
        });

        services.AddScoped(sp => (INamedCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="SourceDocumentCatalog{TModel,TIndex}"/> using
    /// the <see cref="YesSqlStoreOptions.DefaultCollectionName"/>.
    /// </summary>
    public static IServiceCollection AddYesSqlSourceDocumentCatalog<TModel, TIndex>(this IServiceCollection services)
        where TModel : CatalogItem, ISourceAwareModel
        where TIndex : CatalogItemIndex, ISourceAwareIndex
    {
        return AddYesSqlSourceDocumentCatalog<TModel, TIndex>(services, static o => o.DefaultCollectionName);
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="SourceDocumentCatalog{TModel,TIndex}"/> using
    /// the specified <paramref name="collection"/> name.
    /// </summary>
    public static IServiceCollection AddYesSqlSourceDocumentCatalog<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, ISourceAwareModel
        where TIndex : CatalogItemIndex, ISourceAwareIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();

        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();

            return new SourceDocumentCatalog<TModel, TIndex>(session, collection);
        });

        services.AddScoped(sp => (ISourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="NamedSourceDocumentCatalog{TModel,TIndex}"/> using
    /// the <see cref="YesSqlStoreOptions.DefaultCollectionName"/>.
    /// </summary>
    public static IServiceCollection AddYesSqlNamedSourceDocumentCatalog<TModel, TIndex>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
    {
        return AddYesSqlNamedSourceDocumentCatalog<TModel, TIndex>(services, static o => o.DefaultCollectionName);
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="NamedSourceDocumentCatalog{TModel,TIndex}"/> using
    /// the specified <paramref name="collection"/> name.
    /// </summary>
    public static IServiceCollection AddYesSqlNamedSourceDocumentCatalog<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();
        services.RemoveAll<INamedSourceCatalog<TModel>>();

        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();

            return new NamedSourceDocumentCatalog<TModel, TIndex>(session, collection);
        });

        services.AddScoped(sp => (INamedCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        services.AddScoped(sp => (ISourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        services.AddScoped(sp => (INamedSourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

    public static IServiceCollection AddYesSqlNamedDocumentCatalog<TModel, TIndex, TService>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex
        where TService : class, ICatalog<TModel>
    {
        services
            .RemoveAll<ICatalog<TModel>>();

        services.TryAddScoped<TService>();

        services
            .AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<TService>());

        return services;
    }

    public static IServiceCollection AddYesSqlDocumentCatalog<TModel, TIndex, TService>(this IServiceCollection services)
        where TModel : CatalogItem
        where TIndex : CatalogItemIndex
        where TService : class, ICatalog<TModel>
    {
        services
            .RemoveAll<ICatalog<TModel>>();

        services.TryAddScoped<TService>();

        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<TService>());

        return services;
    }

    public static IServiceCollection AddYesSqlSourceDocumentCatalog<TModel, TIndex, TService>(this IServiceCollection services)
        where TModel : CatalogItem, ISourceAwareModel
        where TIndex : CatalogItemIndex, ISourceAwareIndex
        where TService : class, ISourceCatalog<TModel>
    {
        services
            .RemoveAll<ICatalog<TModel>>()
            .RemoveAll<ISourceCatalog<TModel>>();

        services.TryAddScoped<TService>();

        services
            .AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<TService>())
            .AddScoped<ISourceCatalog<TModel>>(sp => sp.GetRequiredService<TService>());

        return services;
    }

    public static IServiceCollection AddYesSqlNamedSourceDocumentCatalog<TModel, TIndex, TService>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
        where TService : class, INamedSourceCatalog<TModel>
    {
        services
            .RemoveAll<ICatalog<TModel>>()
            .RemoveAll<INamedCatalog<TModel>>()
            .RemoveAll<ISourceCatalog<TModel>>()
            .RemoveAll<INamedSourceCatalog<TModel>>();

        services.TryAddScoped<TService>();

        services
            .AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<TService>())
            .AddScoped<INamedCatalog<TModel>>(sp => sp.GetRequiredService<TService>())
            .AddScoped<ISourceCatalog<TModel>>(sp => sp.GetRequiredService<TService>())
            .AddScoped<INamedSourceCatalog<TModel>>(sp => sp.GetRequiredService<TService>());

        return services;
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="NamedSourceDocumentCatalog{TModel,TIndex}"/>
    /// as an <see cref="INamedSourceCatalogSource{TModel}"/> binding source for the
    /// multi-source store pattern.
    /// </summary>
    public static IServiceCollection AddYesSqlNamedSourceBindingSource<TModel, TIndex>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
    {
        return AddYesSqlNamedSourceBindingSource<TModel, TIndex>(services, static o => o.DefaultCollectionName);
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="NamedSourceDocumentCatalog{TModel,TIndex}"/>
    /// as an <see cref="INamedSourceCatalogSource{TModel}"/> binding source for the
    /// multi-source store pattern, using the specified <paramref name="collection"/> name.
    /// </summary>
    public static IServiceCollection AddYesSqlNamedSourceBindingSource<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
    {
        services.AddScoped(sp =>
        {
            var session = sp.GetRequiredService<ISession>();

            return new NamedSourceDocumentCatalog<TModel, TIndex>(session, collection);
        });
        services.AddScoped<INamedSourceCatalogSource<TModel>>(sp =>
            new WritableCatalogBindingSource<TModel>(sp.GetRequiredService<NamedSourceDocumentCatalog<TModel, TIndex>>()));

        return services;
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="NamedDocumentCatalog{TModel,TIndex}"/>
    /// as an <see cref="INamedCatalogSource{TModel}"/> binding source for the
    /// multi-source store pattern.
    /// </summary>
    public static IServiceCollection AddYesSqlNamedBindingSource<TModel, TIndex>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex
    {
        services.AddScoped<NamedDocumentCatalog<TModel, TIndex>>();
        services.AddScoped<INamedCatalogSource<TModel>>(sp =>
            new WritableNamedCatalogBindingSource<TModel>(sp.GetRequiredService<NamedDocumentCatalog<TModel, TIndex>>()));

        return services;
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="NamedDocumentCatalog{TModel,TIndex}"/>
    /// as an <see cref="INamedCatalogSource{TModel}"/> binding source for the
    /// multi-source store pattern, using the specified collection.
    /// </summary>
    public static IServiceCollection AddYesSqlNamedBindingSource<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, INameAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex
    {
        services.AddScoped(sp =>
        {
            var session = sp.GetRequiredService<ISession>();

            return new NamedDocumentCatalog<TModel, TIndex>(session, collection);
        });

        services.AddScoped<INamedCatalogSource<TModel>>(sp =>
            new WritableNamedCatalogBindingSource<TModel>(sp.GetRequiredService<NamedDocumentCatalog<TModel, TIndex>>()));

        return services;
    }

    // ── Private catalog helpers with collection selector ──────────────────────

    private static IServiceCollection AddYesSqlDocumentCatalog<TModel, TIndex>(IServiceCollection services, Func<YesSqlStoreOptions, string> collectionSelector)
        where TModel : CatalogItem
        where TIndex : CatalogItemIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();
            var options = sp.GetRequiredService<IOptions<YesSqlStoreOptions>>().Value;

            return new DocumentCatalog<TModel, TIndex>(session, collectionSelector(options));
        });

        return services;
    }

    private static IServiceCollection AddYesSqlNamedDocumentCatalog<TModel, TIndex>(IServiceCollection services, Func<YesSqlStoreOptions, string> collectionSelector)
        where TModel : CatalogItem, INameAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();

        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();
            var options = sp.GetRequiredService<IOptions<YesSqlStoreOptions>>().Value;

            return new NamedDocumentCatalog<TModel, TIndex>(session, collectionSelector(options));
        });

        services.AddScoped(sp => (INamedCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

    private static IServiceCollection AddYesSqlSourceDocumentCatalog<TModel, TIndex>(IServiceCollection services, Func<YesSqlStoreOptions, string> collectionSelector)
        where TModel : CatalogItem, ISourceAwareModel
        where TIndex : CatalogItemIndex, ISourceAwareIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();

        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();
            var options = sp.GetRequiredService<IOptions<YesSqlStoreOptions>>().Value;

            return new SourceDocumentCatalog<TModel, TIndex>(session, collectionSelector(options));
        });

        services.AddScoped(sp => (ISourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

    private static IServiceCollection AddYesSqlNamedSourceDocumentCatalog<TModel, TIndex>(IServiceCollection services, Func<YesSqlStoreOptions, string> collectionSelector)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();
        services.RemoveAll<INamedSourceCatalog<TModel>>();

        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();
            var options = sp.GetRequiredService<IOptions<YesSqlStoreOptions>>().Value;

            return new NamedSourceDocumentCatalog<TModel, TIndex>(session, collectionSelector(options));
        });

        services.AddScoped(sp => (INamedCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        services.AddScoped(sp => (ISourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        services.AddScoped(sp => (INamedSourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

    private static IServiceCollection AddYesSqlNamedSourceBindingSource<TModel, TIndex>(IServiceCollection services, Func<YesSqlStoreOptions, string> collectionSelector)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
    {
        services.AddScoped(sp =>
        {
            var session = sp.GetRequiredService<ISession>();
            var options = sp.GetRequiredService<IOptions<YesSqlStoreOptions>>().Value;

            return new NamedSourceDocumentCatalog<TModel, TIndex>(session, collectionSelector(options));
        });

        services.AddScoped<INamedSourceCatalogSource<TModel>>(sp =>
            new WritableCatalogBindingSource<TModel>(sp.GetRequiredService<NamedSourceDocumentCatalog<TModel, TIndex>>()));

        return services;
    }
}
