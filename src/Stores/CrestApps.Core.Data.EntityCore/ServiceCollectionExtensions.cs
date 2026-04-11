using CrestApps.Core.AI;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.DataSources;
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

        return services;
    }

    public static IServiceCollection AddCoreEntityCoreSqliteDataStore(this IServiceCollection services, string connectionString, string tablePrefix = "CA_")
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        return services.AddCoreEntityCoreDataStore(options => options.UseSqlite(connectionString), store => store.TablePrefix = tablePrefix);
    }

    public static IServiceCollection AddEntityCoreStores(this IServiceCollection services)
    {
        services
            .AddCatalogManagers();

        // Catalog registrations
        services.AddNamedSourceDocumentCatalog<AIProfile, NamedSourceDocumentCatalog<AIProfile>>();
        services.AddDocumentCatalog<A2AConnection, DocumentCatalog<A2AConnection>>();
        services.AddSourceDocumentCatalog<McpConnection, SourceDocumentCatalog<McpConnection>>();
        services.AddNamedDocumentCatalog<McpPrompt, NamedDocumentCatalog<McpPrompt>>();
        services.AddSourceDocumentCatalog<McpResource, SourceDocumentCatalog<McpResource>>();
        services.AddNamedSourceDocumentCatalog<AIProfileTemplate, NamedSourceDocumentCatalog<AIProfileTemplate>>();
        services.AddDocumentCatalog<ChatInteraction, DocumentCatalog<ChatInteraction>>();

        // Chat session stores
        services.AddScoped<IAIChatSessionManager, EntityCoreAIChatSessionManager>();
        services.AddScoped<IAIChatSessionPromptStore, EntityCoreAIChatSessionPromptStore>();
        services.AddScoped<ICatalog<AIChatSessionPrompt>>(sp => sp.GetRequiredService<IAIChatSessionPromptStore>());

        // Document stores
        services.AddScoped<IAIDocumentStore, EntityCoreAIDocumentStore>();
        services.AddScoped<ICatalog<AIDocument>>(sp => sp.GetRequiredService<IAIDocumentStore>());
        services.AddScoped<IAIDocumentChunkStore, EntityCoreAIDocumentChunkStore>();
        services.AddScoped<ICatalog<AIDocumentChunk>>(sp => sp.GetRequiredService<IAIDocumentChunkStore>());

        // Search index store
        services.AddScoped<ISearchIndexProfileStore, EntityCoreSearchIndexProfileStore>();
        services.AddScoped<ICatalog<SearchIndexProfile>>(sp => sp.GetRequiredService<ISearchIndexProfileStore>());

        // Data source and memory stores
        services.AddScoped<IAIDataSourceStore, EntityCoreAIDataSourceStore>();
        services.AddScoped<ICatalog<AIDataSource>>(sp => sp.GetRequiredService<IAIDataSourceStore>());
        services.AddScoped<IAIMemoryStore, EntityCoreAIMemoryStore>();
        services.AddScoped<ICatalog<AIMemoryEntry>>(sp => sp.GetRequiredService<IAIMemoryStore>());

        // Chat interaction prompt store
        services.AddScoped<IChatInteractionPromptStore, EntityCoreChatInteractionPromptStore>();
        services.AddScoped<ICatalog<ChatInteractionPrompt>>(sp => sp.GetRequiredService<IChatInteractionPromptStore>());

        // AI provider connections: wrap EntityCore catalog as a writable multi-source binding source.
        services.AddEntityCoreNamedSourceBindingSource<AIProviderConnection>();

        // AI deployments: wrap EntityCore catalog as a writable multi-source binding source.
        services.AddEntityCoreNamedSourceBindingSource<AIDeployment>();

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
        services.AddScoped<NamedDocumentCatalog<TModel>>();
        services.AddScoped<INamedCatalogSource<TModel>>(sp =>
            new WritableNamedCatalogBindingSource<TModel>(sp.GetRequiredService<NamedDocumentCatalog<TModel>>()));

        return services;
    }

    public static CrestAppsCoreBuilder AddEntityCoreDataStore(this CrestAppsCoreBuilder builder, Action<DbContextOptionsBuilder> configure, Action<EntityCoreDataStoreOptions> configureStore = null)
    {
        builder.Services.AddCoreEntityCoreDataStore(configure, configureStore);

        return builder;
    }

    public static CrestAppsCoreBuilder AddEntityCoreSqliteDataStore(this CrestAppsCoreBuilder builder, string connectionString, string tablePrefix = "CA_")
    {
        builder.Services.AddCoreEntityCoreSqliteDataStore(connectionString, tablePrefix);

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
