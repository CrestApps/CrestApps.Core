using CrestApps.Core.AI;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Builders;
using CrestApps.Core.Data.EntityCore.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public static IServiceCollection AddDocumentCatalogs(this IServiceCollection services)
    {
        services.TryAddScoped(typeof(ICatalog<>), typeof(DocumentCatalog<>));
        services.TryAddScoped(typeof(INamedCatalog<>), typeof(NamedDocumentCatalog<>));
        services.TryAddScoped(typeof(ISourceCatalog<>), typeof(SourceDocumentCatalog<>));
        services.TryAddScoped(typeof(INamedSourceCatalog<>), typeof(NamedSourceDocumentCatalog<>));

        return services;
    }

    public static IServiceCollection AddDocumentCatalog<TModel>(this IServiceCollection services)
        where TModel : CatalogItem
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.AddScoped<ICatalog<TModel>, DocumentCatalog<TModel>>();
        return services;
    }

    public static IServiceCollection AddNamedDocumentCatalog<TModel>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();
        services.AddScoped<ICatalog<TModel>, NamedDocumentCatalog<TModel>>();
        services.AddScoped<INamedCatalog<TModel>>(sp => (INamedCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        return services;
    }

    public static IServiceCollection AddSourceDocumentCatalog<TModel>(this IServiceCollection services)
        where TModel : CatalogItem, ISourceAwareModel
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();
        services.AddScoped<ICatalog<TModel>, SourceDocumentCatalog<TModel>>();
        services.AddScoped<ISourceCatalog<TModel>>(sp => (ISourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        return services;
    }

    public static IServiceCollection AddNamedSourceDocumentCatalog<TModel>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();
        services.RemoveAll<INamedSourceCatalog<TModel>>();
        services.AddScoped<ICatalog<TModel>, NamedSourceDocumentCatalog<TModel>>();
        services.AddScoped<INamedCatalog<TModel>>(sp => (INamedCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        services.AddScoped<ISourceCatalog<TModel>>(sp => (ISourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        services.AddScoped<INamedSourceCatalog<TModel>>(sp => (INamedSourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        return services;
    }

    public static IServiceCollection AddDocumentCatalog<TModel, TIndex, T>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem
        where T : class, ICatalog<TModel>
    {
        _ = collection;

        services.RemoveAll<ICatalog<TModel>>();
        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());
        return services;
    }

    public static IServiceCollection AddNamedDocumentCatalog<TModel, TIndex, T>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, INameAwareModel
        where T : class, INamedCatalog<TModel>
    {
        _ = collection;

        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();
        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<INamedCatalog<TModel>>(sp => sp.GetRequiredService<T>());
        return services;
    }

    public static IServiceCollection AddSourceDocumentCatalog<TModel, TIndex, T>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, ISourceAwareModel
        where T : class, ISourceCatalog<TModel>
    {
        _ = collection;

        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();
        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<ISourceCatalog<TModel>>(sp => sp.GetRequiredService<T>());
        return services;
    }

    public static IServiceCollection AddNamedSourceDocumentCatalog<TModel, TIndex, T>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where T : class, INamedSourceCatalog<TModel>
    {
        _ = collection;

        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();
        services.RemoveAll<INamedSourceCatalog<TModel>>();
        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<INamedCatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<ISourceCatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<INamedSourceCatalog<TModel>>(sp => sp.GetRequiredService<T>());
        return services;
    }

    public static IServiceCollection AddCoreEntityCoreStores(this IServiceCollection services)
    {
        services.AddCatalogManagers();

        // Catalog registrations
        services.AddNamedSourceDocumentCatalog<AIProfile>();
        services.AddNamedSourceDocumentCatalog<AIProviderConnection>();
        services.AddDocumentCatalog<A2AConnection>();
        services.AddSourceDocumentCatalog<McpConnection>();
        services.AddNamedDocumentCatalog<McpPrompt>();
        services.AddSourceDocumentCatalog<McpResource>();
        services.AddNamedSourceDocumentCatalog<AIProfileTemplate>();
        services.AddDocumentCatalog<ChatInteraction>();

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

        services.AddKeyedScoped<INamedSourceCatalog<AIProviderConnection>, Services.NamedSourceDocumentCatalog<AIProviderConnection>>(ConfigurationAIProviderConnectionCatalog.PersistedCatalogKey);
        services.AddNamedSourceDocumentCatalog<AIProviderConnection, object, ConfigurationAIProviderConnectionCatalog>();

        services.AddKeyedScoped<INamedSourceCatalog<AIDeployment>, Services.NamedSourceDocumentCatalog<AIDeployment>>(ConfigurationAIDeploymentCatalog.PersistedCatalogKey);
        services.AddNamedSourceDocumentCatalog<AIDeployment, object, ConfigurationAIDeploymentCatalog>();

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
