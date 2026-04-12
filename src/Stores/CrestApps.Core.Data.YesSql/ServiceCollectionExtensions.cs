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
using YesSql;
using YesSql.Indexes;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreYesSqlDataStore(this IServiceCollection services, Func<Configuration, IConfiguration> configure)
    {
        services.AddSingleton<IStore>(sp =>
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
    public static CrestAppsAISuiteBuilder AddYesSqlStores(this CrestAppsAISuiteBuilder builder, string collection = null)
    {
        builder.Services.AddCoreAIServicesStoresYesSql(collection);
        builder.Services.AddCoreAIChatSessionStoresYesSql();

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the A2A client feature on the A2A client builder.
    /// This includes a catalog for <see cref="A2AConnection"/>.
    /// </summary>
    public static CrestAppsA2AClientBuilder AddYesSqlStores(this CrestAppsA2AClientBuilder builder, string collection = null)
    {
        builder.Services.AddCoreAIA2AClientStoresYesSql(collection);

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the MCP client feature on the MCP client builder.
    /// This includes catalogs for <see cref="McpConnection"/>, <see cref="McpPrompt"/>, and <see cref="McpResource"/>.
    /// </summary>
    public static CrestAppsMcpClientBuilder AddYesSqlStores(this CrestAppsMcpClientBuilder builder, string collection = null)
    {
        builder.Services.AddCoreAIMcpClientStoresYesSql(collection);

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the chat interactions feature on the chat interactions builder.
    /// This includes a catalog for <see cref="ChatInteraction"/> and <see cref="IChatInteractionPromptStore"/>.
    /// </summary>
    public static CrestAppsChatInteractionsBuilder AddYesSqlStores(this CrestAppsChatInteractionsBuilder builder, string collection = null)
    {
        builder.Services.AddCoreAIChatInteractionStoresYesSql(collection);

        return builder;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the document processing feature on the document processing builder.
    /// This includes <see cref="IAIDocumentStore"/>, <see cref="IAIDocumentChunkStore"/>,
    /// <see cref="ISearchIndexProfileStore"/>, and <see cref="IAIDataSourceStore"/>.
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
    /// Registers YesSql-backed stores for the core AI services feature.
    /// This includes catalogs for <see cref="AIProfile"/>, <see cref="AIProfileTemplate"/>,
    /// and multi-source binding sources for <see cref="AIProviderConnection"/> and <see cref="AIDeployment"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIServicesStoresYesSql(this IServiceCollection services, string collection = null)
    {
        services.AddYesSqlNamedSourceDocumentCatalog<AIProfile, AIProfileIndex>(collection);
        services.AddYesSqlNamedSourceDocumentCatalog<AIProfileTemplate, AIProfileTemplateIndex>(collection);
        services.AddYesSqlNamedSourceBindingSource<AIProviderConnection, AIProviderConnectionIndex>(collection);
        services.AddYesSqlNamedSourceBindingSource<AIDeployment, AIDeploymentIndex>(collection);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIProfileIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIProfileTemplateIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIProviderConnectionIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIDeploymentIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the A2A client feature.
    /// This includes a catalog for <see cref="A2AConnection"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIA2AClientStoresYesSql(this IServiceCollection services, string collection = null)
    {
        services.AddYesSqlDocumentCatalog<A2AConnection, A2AConnectionIndex>(collection);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, A2AConnectionIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the MCP client feature.
    /// This includes catalogs for <see cref="McpConnection"/>, <see cref="McpPrompt"/>, and <see cref="McpResource"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIMcpClientStoresYesSql(this IServiceCollection services, string collection = null)
    {
        services.AddYesSqlSourceDocumentCatalog<McpConnection, McpConnectionIndex>(collection);
        services.AddYesSqlNamedDocumentCatalog<McpPrompt, McpPromptIndex>(collection);
        services.AddYesSqlSourceDocumentCatalog<McpResource, McpResourceIndex>(collection);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, McpConnectionIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, McpPromptIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, McpResourceIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the AI chat sessions feature.
    /// This includes <see cref="IAIChatSessionManager"/> and <see cref="IAIChatSessionPromptStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIChatSessionStoresYesSql(this IServiceCollection services)
    {
        services.AddScoped<IAIChatSessionManager, YesSqlAIChatSessionManager>();
        services.AddScoped<IAIChatSessionPromptStore, YesSqlAIChatSessionPromptStore>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIChatSessionIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIChatSessionMetricsIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AICompletionUsageIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIChatSessionExtractedDataIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIChatSessionPromptIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the document processing feature.
    /// This includes <see cref="IAIDocumentStore"/>, <see cref="IAIDocumentChunkStore"/>,
    /// and <see cref="ISearchIndexProfileStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIDocumentProcessingStoresYesSql(this IServiceCollection services)
    {
        services.AddScoped<IAIDocumentStore, YesSqlAIDocumentStore>();
        services.AddScoped<IAIDocumentChunkStore, YesSqlAIDocumentChunkStore>();
        services.AddScoped<ISearchIndexProfileStore, YesSqlSearchIndexProfileStore>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIDocumentIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, AIDocumentChunkIndexProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, SearchIndexProfileIndexProvider>());

        return services;
    }

    /// <summary>
    /// Registers YesSql-backed stores for the data source RAG feature.
    /// This includes <see cref="IAIDataSourceStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIDataSourceStoresYesSql(this IServiceCollection services)
    {
        services.AddScoped<IAIDataSourceStore, YesSqlAIDataSourceStore>();
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
    public static IServiceCollection AddCoreAIChatInteractionStoresYesSql(this IServiceCollection services, string collection = null)
    {
        services.AddYesSqlDocumentCatalog<ChatInteraction, ChatInteractionIndex>(collection);
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

        services.AddScoped<INamedCatalog<TModel>>(sp => (INamedCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

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

        services.AddScoped<ISourceCatalog<TModel>>(sp => (ISourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

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

        services.AddScoped<INamedCatalog<TModel>>(sp => (INamedCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        services.AddScoped<ISourceCatalog<TModel>>(sp => (ISourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        services.AddScoped<INamedSourceCatalog<TModel>>(sp => (INamedSourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

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
    public static IServiceCollection AddYesSqlNamedSourceBindingSource<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
    {
        services.AddScoped(sp => new NamedSourceDocumentCatalog<TModel, TIndex>(sp.GetRequiredService<ISession>(), collection));
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
}
