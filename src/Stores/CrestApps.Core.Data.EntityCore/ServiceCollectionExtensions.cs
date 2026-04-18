using CrestApps.Core.AI;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Builders;
using CrestApps.Core.Data.EntityCore.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreEntityCoreDataStore(this IServiceCollection services, Action<DbContextOptionsBuilder> configure, Action<EntityCoreDataStoreOptions> configureStore = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<EntityCoreDataStoreOptions>();

        if (configureStore is not null)
        {
            services.Configure(configureStore);
        }

        services.AddDbContext<CrestAppsEntityDbContext>(configure);
        services.AddScoped<IStoreCommitter, EntityCoreStoreCommitter>();

        return services;
    }

    public static IServiceCollection AddCoreEntityCoreSqliteDataStore(this IServiceCollection services, string connectionString, string tablePrefix = "CA_")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tablePrefix);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        return services.AddCoreEntityCoreDataStore(options => options.UseSqlite(connectionString), store => store.TablePrefix = tablePrefix);
    }

    public static IServiceCollection AddEntityCoreStores(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddCatalogManagers();

        services.AddCoreAIServicesStoresEntityCore();
        services.AddCoreAIProfileTemplateStoresEntityCore();
        services.AddCoreAIA2AClientStoresEntityCore();
        services.AddCoreAIMcpClientStoresEntityCore();
        services.AddCoreAIMcpServerStoresEntityCore();
        services.AddCoreAIChatSessionStoresEntityCore();
        services.AddCoreAIDocumentProcessingStoresEntityCore();
        services.AddCoreAIDataSourceStoresEntityCore();
        services.AddCoreAIMemoryStoresEntityCore();
        services.AddCoreAIChatInteractionStoresEntityCore();
        services.AddCoreIndexingStoresEntityCore();

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the core AI services feature.
    /// This includes a catalog for <see cref="AIProfile"/>
    /// and multi-source binding sources for <see cref="AIProviderConnection"/> and <see cref="AIDeployment"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIServicesStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNamedSourceDocumentCatalog<AIProfile, NamedSourceDocumentCatalog<AIProfile>>();
        services.AddEntityCoreNamedSourceBindingSource<AIProviderConnection>();
        services.AddEntityCoreNamedSourceBindingSource<AIDeployment>();

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the AI profile template feature.
    /// This includes a catalog for <see cref="AIProfileTemplate"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIProfileTemplateStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNamedSourceDocumentCatalog<AIProfileTemplate, NamedSourceDocumentCatalog<AIProfileTemplate>>();

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the A2A client feature.
    /// This includes a catalog for <see cref="A2AConnection"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIA2AClientStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDocumentCatalog<A2AConnection, DocumentCatalog<A2AConnection>>();

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the MCP client feature.
    /// This includes a catalog for <see cref="McpConnection"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIMcpClientStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSourceDocumentCatalog<McpConnection, SourceDocumentCatalog<McpConnection>>();

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the MCP server feature.
    /// This includes catalogs for <see cref="McpPrompt"/> and <see cref="McpResource"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIMcpServerStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNamedDocumentCatalog<McpPrompt, NamedDocumentCatalog<McpPrompt>>();
        services.AddSourceDocumentCatalog<McpResource, SourceDocumentCatalog<McpResource>>();

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the AI chat sessions feature.
    /// This includes <see cref="IAIChatSessionManager"/> and <see cref="IAIChatSessionPromptStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIChatSessionStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAIChatSessionManager, EntityCoreAIChatSessionManager>();
        services.AddScoped<IAIChatSessionPromptStore, EntityCoreAIChatSessionPromptStore>();
        services.AddScoped<ICatalog<AIChatSessionPrompt>>(sp => sp.GetRequiredService<IAIChatSessionPromptStore>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the document processing feature.
    /// This includes <see cref="IAIDocumentStore"/> and <see cref="IAIDocumentChunkStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIDocumentProcessingStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAIDocumentStore, EntityCoreAIDocumentStore>();
        services.AddScoped<ICatalog<AIDocument>>(sp => sp.GetRequiredService<IAIDocumentStore>());
        services.AddScoped<IAIDocumentChunkStore, EntityCoreAIDocumentChunkStore>();
        services.AddScoped<ICatalog<AIDocumentChunk>>(sp => sp.GetRequiredService<IAIDocumentChunkStore>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the indexing services feature.
    /// This includes <see cref="ISearchIndexProfileStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreIndexingStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ISearchIndexProfileStore, EntityCoreSearchIndexProfileStore>();
        services.AddScoped<ICatalog<SearchIndexProfile>>(sp => sp.GetRequiredService<ISearchIndexProfileStore>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the data source RAG feature.
    /// This includes <see cref="IAIDataSourceStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIDataSourceStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAIDataSourceStore, EntityCoreAIDataSourceStore>();
        services.AddScoped<ICatalog<AIDataSource>>(sp => sp.GetRequiredService<IAIDataSourceStore>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the AI memory feature.
    /// This includes <see cref="IAIMemoryStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIMemoryStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAIMemoryStore, EntityCoreAIMemoryStore>();
        services.AddScoped<ICatalog<AIMemoryEntry>>(sp => sp.GetRequiredService<IAIMemoryStore>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the chat interactions feature.
    /// This includes a catalog for <see cref="ChatInteraction"/>
    /// and <see cref="IChatInteractionPromptStore"/>.
    /// </summary>
    public static IServiceCollection AddCoreAIChatInteractionStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDocumentCatalog<ChatInteraction, DocumentCatalog<ChatInteraction>>();
        services.AddScoped<IChatInteractionPromptStore, EntityCoreChatInteractionPromptStore>();
        services.AddScoped<ICatalog<ChatInteractionPrompt>>(sp => sp.GetRequiredService<IChatInteractionPromptStore>());

        return services;
    }

    /// <summary>
    /// Registers an EntityCore-backed <see cref="NamedSourceDocumentCatalog{TModel}"/>
    /// as an <see cref="INamedSourceCatalogSource{TModel}"/> binding source for the
    /// multi-source store pattern.
    /// </summary>
    public static IServiceCollection AddEntityCoreNamedSourceBindingSource<TModel>(this IServiceCollection services)
        where TModel : SourceCatalogEntry, INameAwareModel
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<NamedSourceDocumentCatalog<TModel>>();
        services.AddScoped<INamedSourceCatalogSource<TModel>>(sp =>
            new WritableCatalogBindingSource<TModel>(sp.GetRequiredService<NamedSourceDocumentCatalog<TModel>>()));

        return services;
    }

    /// <summary>
    /// Registers an EntityCore-backed <see cref="NamedDocumentCatalog{TModel}"/>
    /// as an <see cref="INamedCatalogSource{TModel}"/> binding source for the
    /// multi-source store pattern.
    /// </summary>
    public static IServiceCollection AddEntityCoreNamedBindingSource<TModel>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<NamedDocumentCatalog<TModel>>();
        services.AddScoped<INamedCatalogSource<TModel>>(sp =>
            new WritableNamedCatalogBindingSource<TModel>(sp.GetRequiredService<NamedDocumentCatalog<TModel>>()));

        return services;
    }

    public static CrestAppsCoreBuilder AddEntityCoreDataStore(this CrestAppsCoreBuilder builder, Action<DbContextOptionsBuilder> configure, Action<EntityCoreDataStoreOptions> configureStore = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddCoreEntityCoreDataStore(configure, configureStore);

        return builder;
    }

    public static CrestAppsCoreBuilder AddEntityCoreSqliteDataStore(this CrestAppsCoreBuilder builder, string connectionString, string tablePrefix = "CA_")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(tablePrefix);

        builder.Services.AddCoreEntityCoreSqliteDataStore(connectionString, tablePrefix);

        return builder;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the core AI services feature on the AI suite builder.
    /// This includes catalogs for <see cref="AIProfile"/>, <see cref="AIProfileTemplate"/>,
    /// multi-source binding sources for <see cref="AIProviderConnection"/> and <see cref="AIDeployment"/>,
    /// and the chat session stores (<see cref="IAIChatSessionManager"/> and <see cref="IAIChatSessionPromptStore"/>).
    /// </summary>
    public static CrestAppsAISuiteBuilder AddEntityCoreStores(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIServicesStoresEntityCore();
        builder.Services.AddCoreAIProfileTemplateStoresEntityCore();
        builder.Services.AddCoreAIChatSessionStoresEntityCore();

        return builder;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the A2A client feature on the A2A client builder.
    /// This includes a catalog for <see cref="A2AConnection"/>.
    /// </summary>
    public static CrestAppsA2AClientBuilder AddEntityCoreStores(this CrestAppsA2AClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIA2AClientStoresEntityCore();

        return builder;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the MCP client feature on the MCP client builder.
    /// This includes a catalog for <see cref="McpConnection"/>.
    /// </summary>
    public static CrestAppsMcpClientBuilder AddEntityCoreStores(this CrestAppsMcpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIMcpClientStoresEntityCore();

        return builder;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the MCP server feature on the MCP server builder.
    /// This includes catalogs for <see cref="McpPrompt"/> and <see cref="McpResource"/>.
    /// </summary>
    public static CrestAppsMcpServerBuilder AddEntityCoreStores(this CrestAppsMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIMcpServerStoresEntityCore();

        return builder;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the chat interactions feature on the chat interactions builder.
    /// This includes a catalog for <see cref="ChatInteraction"/> and <see cref="IChatInteractionPromptStore"/>.
    /// </summary>
    public static CrestAppsChatInteractionsBuilder AddEntityCoreStores(this CrestAppsChatInteractionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIChatInteractionStoresEntityCore();

        return builder;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the document processing feature on the document processing builder.
    /// This includes <see cref="IAIDocumentStore"/>, <see cref="IAIDocumentChunkStore"/>,
    /// and <see cref="IAIDataSourceStore"/>.
    /// </summary>
    public static CrestAppsDocumentProcessingBuilder AddEntityCoreStores(this CrestAppsDocumentProcessingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIDocumentProcessingStoresEntityCore();
        builder.Services.AddCoreAIDataSourceStoresEntityCore();

        return builder;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the AI memory feature on the AI memory builder.
    /// This includes <see cref="IAIMemoryStore"/>.
    /// </summary>
    public static CrestAppsAIMemoryBuilder AddEntityCoreStores(this CrestAppsAIMemoryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIMemoryStoresEntityCore();

        return builder;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the indexing services feature on the indexing builder.
    /// This includes <see cref="ISearchIndexProfileStore"/>.
    /// </summary>
    public static CrestAppsIndexingBuilder AddEntityCoreStores(this CrestAppsIndexingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreIndexingStoresEntityCore();

        return builder;
    }

    public static async Task InitializeEntityCoreSchemaAsync(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CrestAppsEntityDbContext>();

        await dbContext.Database.EnsureCreatedAsync();
    }
}
