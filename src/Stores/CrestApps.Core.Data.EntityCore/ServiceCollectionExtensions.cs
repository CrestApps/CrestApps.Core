using CrestApps.Core.AI;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Builders;
using CrestApps.Core.Data.EntityCore.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Data.EntityCore;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core entity core data store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The action used to configure.</param>
    /// <param name="configureStore">The action used to configure store.</param>
    public static IServiceCollection AddCoreEntityCoreDataStore(this IServiceCollection services, Action<DbContextOptionsBuilder> configure, Action<EntityCoreDataStoreOptions> configureStore = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<EntityCoreDataStoreOptions>();

        if (configureStore is not null)
        {
            services.Configure(configureStore);
        }

        services.AddDbContext<CrestAppsEntityDbContext>((sp, options) =>
        {
            configure(options);

            var snapshot = sp.GetRequiredService<IOptions<EntityCoreDataStoreOptions>>().Value;
            var coreOptionsBuilder = ((IDbContextOptionsBuilderInfrastructure)options);
            coreOptionsBuilder.AddOrUpdateExtension(new CrestAppsOptionsExtension(
                snapshot.TablePrefix,
                snapshot.EnforceNamedSourceUniqueness));

            options.ReplaceService<IModelCacheKeyFactory, CrestAppsModelCacheKeyFactory>();
        });
        services.AddScoped<IStoreCommitter, EntityCoreStoreCommitter>();

        return services;
    }

    /// <summary>
    /// Adds core entity core sqlite data store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="tablePrefix">The table prefix.</param>
    public static IServiceCollection AddCoreEntityCoreSqliteDataStore(this IServiceCollection services, string connectionString, string tablePrefix = "CA_")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tablePrefix);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        return services.AddCoreEntityCoreDataStore(options => options.UseSqlite(connectionString), store => store.TablePrefix = tablePrefix);
    }

    /// <summary>
    /// Adds entity core stores.
    /// </summary>
    /// <param name="services">The service collection.</param>
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
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIServicesStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IAIProfileStore>();
        services.RemoveAll<ICatalog<AIProfile>>();
        services.RemoveAll<INamedCatalog<AIProfile>>();
        services.RemoveAll<ISourceCatalog<AIProfile>>();
        services.RemoveAll<INamedSourceCatalog<AIProfile>>();

        services.AddScoped<EntityCoreAIProfileStore>();
        services.AddScoped<IAIProfileStore>(sp => sp.GetRequiredService<EntityCoreAIProfileStore>());
        services.AddScoped<ICatalog<AIProfile>>(sp => sp.GetRequiredService<EntityCoreAIProfileStore>());
        services.AddScoped<INamedCatalog<AIProfile>>(sp => sp.GetRequiredService<EntityCoreAIProfileStore>());
        services.AddScoped<ISourceCatalog<AIProfile>>(sp => sp.GetRequiredService<EntityCoreAIProfileStore>());
        services.AddScoped<INamedSourceCatalog<AIProfile>>(sp => sp.GetRequiredService<EntityCoreAIProfileStore>());
        services.AddEntityCoreNamedSourceBindingSource<AIProviderConnection>();
        services.AddEntityCoreNamedSourceBindingSource<AIDeployment>();

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the AI profile template feature.
    /// This includes a catalog for <see cref="AIProfileTemplate"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
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
    /// <param name="services">The service collection.</param>
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
    /// <param name="services">The service collection.</param>
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
    /// <param name="services">The service collection.</param>
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
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIChatSessionStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Scoped<IAIChatSessionManager, EntityCoreAIChatSessionManager>());
        services.Replace(ServiceDescriptor.Scoped<IAIChatSessionPromptStore, EntityCoreAIChatSessionPromptStore>());
        services.Replace(ServiceDescriptor.Scoped<IAIChatSessionEventStore, EntityCoreAIChatSessionEventStore>());
        services.Replace(ServiceDescriptor.Scoped<IAICompletionUsageStore, EntityCoreAICompletionUsageStore>());
        services.AddCoreAIChatSessionExtractedDataStoresEntityCore();
        services.AddScoped<ICatalog<AIChatSessionPrompt>>(sp => sp.GetRequiredService<IAIChatSessionPromptStore>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for chat session extracted-data snapshots.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIChatSessionExtractedDataStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Scoped<IAIChatSessionExtractedDataStore, EntityCoreAIChatSessionExtractedDataStore>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIChatSessionExtractedDataRecorder, DefaultAIChatSessionExtractedDataRecorder>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the document processing feature.
    /// This includes <see cref="IAIDocumentStore"/> and <see cref="IAIDocumentChunkStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIDocumentProcessingStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Scoped<IAIDocumentStore, EntityCoreAIDocumentStore>());
        services.AddScoped<ICatalog<AIDocument>>(sp => sp.GetRequiredService<IAIDocumentStore>());
        services.Replace(ServiceDescriptor.Scoped<IAIDocumentChunkStore, EntityCoreAIDocumentChunkStore>());
        services.AddScoped<ICatalog<AIDocumentChunk>>(sp => sp.GetRequiredService<IAIDocumentChunkStore>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the indexing services feature.
    /// This includes <see cref="ISearchIndexProfileStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreIndexingStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Scoped<ISearchIndexProfileStore, EntityCoreSearchIndexProfileStore>());
        services.AddScoped<ICatalog<SearchIndexProfile>>(sp => sp.GetRequiredService<ISearchIndexProfileStore>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the data source RAG feature.
    /// This includes <see cref="IAIDataSourceStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIDataSourceStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Scoped<IAIDataSourceStore, EntityCoreAIDataSourceStore>());
        services.AddScoped<ICatalog<AIDataSource>>(sp => sp.GetRequiredService<IAIDataSourceStore>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the AI memory feature.
    /// This includes <see cref="IAIMemoryStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIMemoryStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Scoped<IAIMemoryStore, EntityCoreAIMemoryStore>());
        services.AddScoped<ICatalog<AIMemoryEntry>>(sp => sp.GetRequiredService<IAIMemoryStore>());

        return services;
    }

    /// <summary>
    /// Registers EntityCore-backed stores for the chat interactions feature.
    /// This includes a catalog for <see cref="ChatInteraction"/>
    /// and <see cref="IChatInteractionPromptStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIChatInteractionStoresEntityCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDocumentCatalog<ChatInteraction, DocumentCatalog<ChatInteraction>>();
        services.Replace(ServiceDescriptor.Scoped<IChatInteractionPromptStore, EntityCoreChatInteractionPromptStore>());
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

        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();
        services.RemoveAll<INamedSourceCatalog<TModel>>();

        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<NamedSourceDocumentCatalog<TModel>>());
        services.AddScoped<INamedCatalog<TModel>>(sp => sp.GetRequiredService<NamedSourceDocumentCatalog<TModel>>());
        services.AddScoped<ISourceCatalog<TModel>>(sp => sp.GetRequiredService<NamedSourceDocumentCatalog<TModel>>());
        services.AddScoped<INamedSourceCatalog<TModel>>(sp => sp.GetRequiredService<NamedSourceDocumentCatalog<TModel>>());

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

    /// <summary>
    /// Adds entity core data store.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    /// <param name="configureStore">The configure store.</param>
    public static CrestAppsCoreBuilder AddEntityCoreDataStore(this CrestAppsCoreBuilder builder, Action<DbContextOptionsBuilder> configure, Action<EntityCoreDataStoreOptions> configureStore = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddCoreEntityCoreDataStore(configure, configureStore);

        return builder;
    }

    /// <summary>
    /// Adds entity core sqlite data store.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="tablePrefix">The table prefix.</param>
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
    /// <param name="builder">The builder.</param>
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
    /// <param name="builder">The builder.</param>
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
    /// <param name="builder">The builder.</param>
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
    /// <param name="builder">The builder.</param>
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
    /// <param name="builder">The builder.</param>
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
    /// <param name="builder">The builder.</param>
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
    /// <param name="builder">The builder.</param>
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
    /// <param name="builder">The builder.</param>
    public static CrestAppsIndexingBuilder AddEntityCoreStores(this CrestAppsIndexingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreIndexingStoresEntityCore();

        return builder;
    }

    /// <summary>
    /// Initializes entity core schema.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static async Task InitializeEntityCoreSchemaAsync(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CrestAppsEntityDbContext>();
        var storeOptions = scope.ServiceProvider.GetRequiredService<IOptions<EntityCoreDataStoreOptions>>().Value;

        await dbContext.Database.EnsureCreatedAsync();

        var tablePrefix = storeOptions.TablePrefix ?? string.Empty;

#pragma warning disable CS0618
        await MigrateFromLegacySchemaIfNeededAsync(dbContext, tablePrefix);
#pragma warning restore CS0618
        await EnsureOptionalTablesAsync(dbContext, tablePrefix);
    }

    /// <summary>
    /// Detects the pre-v1 (Payload-per-table) schema and migrates data into the
    /// centralized <c>Documents</c> table, adding identity and foreign-key columns
    /// to every index table. Existing databases are transformed in a single
    /// transaction; brand-new databases created by <c>EnsureCreatedAsync</c>
    /// are not affected.
    /// </summary>
    [Obsolete("Schema migration from pre-v1 layout. Will be removed before v1.0.0 ships.")]
    private static async Task MigrateFromLegacySchemaIfNeededAsync(
        CrestAppsEntityDbContext dbContext,
        string tablePrefix)
    {
        var catalogTableName = GetSafeSqlIdentifier($"{tablePrefix}CatalogRecords");

        if (!await HasColumnAsync(dbContext, catalogTableName, "Payload"))
        {
            return;
        }

        var documentsTableName = GetSafeSqlIdentifier($"{tablePrefix}Documents");
        var sessionsTableName = GetSafeSqlIdentifier($"{tablePrefix}AIChatSessions");
        var eventsTableName = GetSafeSqlIdentifier($"{tablePrefix}AIChatSessionEvents");
        var usageTableName = GetSafeSqlIdentifier($"{tablePrefix}AICompletionUsage");
        var extractedTableName = GetSafeSqlIdentifier($"{tablePrefix}AIChatSessionExtractedData");

        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        try
        {
            await ExecuteNonQueryAsync(connection, transaction,
                $"""
                CREATE TABLE IF NOT EXISTS "{documentsTableName}" (
                    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                    "Type" TEXT NOT NULL,
                    "Content" TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}Documents_Type")}" ON "{documentsTableName}" ("Type");
                """);

            await MigrateCatalogRecordsAsync(connection, transaction, catalogTableName, documentsTableName, tablePrefix);
            await MigrateAIChatSessionsAsync(connection, transaction, sessionsTableName, documentsTableName, tablePrefix);

            if (await TableExistsAsync(connection, transaction, eventsTableName))
            {
                await MigrateAIChatSessionEventsAsync(connection, transaction, eventsTableName, documentsTableName, tablePrefix);
            }

            if (await TableExistsAsync(connection, transaction, usageTableName))
            {
                await MigrateAICompletionUsageAsync(connection, transaction, usageTableName, documentsTableName, tablePrefix);
            }

            if (await TableExistsAsync(connection, transaction, extractedTableName))
            {
                await MigrateAIChatSessionExtractedDataAsync(connection, transaction, extractedTableName, documentsTableName, tablePrefix);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    [Obsolete("Schema migration from pre-v1 layout. Will be removed before v1.0.0 ships.")]
    private static async Task MigrateCatalogRecordsAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        string documentsTableName,
        string tablePrefix)
    {
        var backupName = $"{tableName}_v0";

        await ExecuteNonQueryAsync(connection, transaction,
            $"""ALTER TABLE "{tableName}" RENAME TO "{backupName}";""");

        await ExecuteNonQueryAsync(connection, transaction,
            $"""
            CREATE TABLE "{tableName}" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "DocumentId" INTEGER NOT NULL,
                "EntityType" TEXT NOT NULL,
                "ItemId" TEXT NOT NULL,
                "Name" TEXT NULL,
                "DisplayText" TEXT NULL,
                "Source" TEXT NULL,
                "SessionId" TEXT NULL,
                "ChatInteractionId" TEXT NULL,
                "ReferenceId" TEXT NULL,
                "ReferenceType" TEXT NULL,
                "AIDocumentId" TEXT NULL,
                "UserId" TEXT NULL,
                "Type" TEXT NULL,
                "CreatedUtc" TEXT NULL,
                "UpdatedUtc" TEXT NULL,
                FOREIGN KEY ("DocumentId") REFERENCES "{documentsTableName}" ("Id")
            );
            """);

        var availableCols = await GetColumnNamesAsync(connection, transaction, backupName);

        string Col(string name) =>
            availableCols.Contains(name) ? $"\"{name}\"" : $"NULL AS \"{name}\"";

        using (var readCmd = connection.CreateCommand())
        {
            readCmd.Transaction = transaction;
            readCmd.CommandText =
                $"""
                SELECT "EntityType", "ItemId", {Col("Name")}, {Col("DisplayText")}, {Col("Source")},
                       {Col("SessionId")}, {Col("ChatInteractionId")}, {Col("ReferenceId")}, {Col("ReferenceType")},
                       {Col("AIDocumentId")}, {Col("UserId")}, {Col("Type")}, {Col("CreatedUtc")}, {Col("UpdatedUtc")},
                       "Payload"
                FROM "{backupName}";
                """;

            using var reader = await readCmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var docId = await InsertDocumentAsync(connection, transaction, documentsTableName,
                    reader.GetString(0), reader.GetString(14));

                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText =
                    $"""
                    INSERT INTO "{tableName}" ("DocumentId","EntityType","ItemId","Name","DisplayText","Source","SessionId","ChatInteractionId","ReferenceId","ReferenceType","AIDocumentId","UserId","Type","CreatedUtc","UpdatedUtc")
                    VALUES (@d,@et,@ii,@n,@dt,@s,@si,@ci,@ri,@rt,@ai,@ui,@t,@cu,@uu);
                    """;

                AddParam(insertCmd, "@d", docId);
                AddParam(insertCmd, "@et", reader, 0);
                AddParam(insertCmd, "@ii", reader, 1);
                AddParam(insertCmd, "@n", reader, 2);
                AddParam(insertCmd, "@dt", reader, 3);
                AddParam(insertCmd, "@s", reader, 4);
                AddParam(insertCmd, "@si", reader, 5);
                AddParam(insertCmd, "@ci", reader, 6);
                AddParam(insertCmd, "@ri", reader, 7);
                AddParam(insertCmd, "@rt", reader, 8);
                AddParam(insertCmd, "@ai", reader, 9);
                AddParam(insertCmd, "@ui", reader, 10);
                AddParam(insertCmd, "@t", reader, 11);
                AddParam(insertCmd, "@cu", reader, 12);
                AddParam(insertCmd, "@uu", reader, 13);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        await ExecuteNonQueryAsync(connection, transaction, $"""DROP TABLE "{backupName}";""");

        await CreateCatalogIndexes(connection, transaction, tableName, tablePrefix);
    }

    [Obsolete("Schema migration from pre-v1 layout. Will be removed before v1.0.0 ships.")]
    private static async Task MigrateAIChatSessionsAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        string documentsTableName,
        string tablePrefix)
    {
        var backupName = $"{tableName}_v0";

        await ExecuteNonQueryAsync(connection, transaction,
            $"""ALTER TABLE "{tableName}" RENAME TO "{backupName}";""");

        await ExecuteNonQueryAsync(connection, transaction,
            $"""
            CREATE TABLE "{tableName}" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "DocumentId" INTEGER NOT NULL,
                "SessionId" TEXT NOT NULL,
                "ProfileId" TEXT NULL,
                "Title" TEXT NULL,
                "UserId" TEXT NULL,
                "ClientId" TEXT NULL,
                "Status" INTEGER NOT NULL DEFAULT 0,
                "CreatedUtc" TEXT NOT NULL,
                "LastActivityUtc" TEXT NOT NULL,
                FOREIGN KEY ("DocumentId") REFERENCES "{documentsTableName}" ("Id")
            );
            """);

        var sessionType = typeof(CrestApps.Core.AI.Models.AIChatSession).FullName!;
        var availableCols = await GetColumnNamesAsync(connection, transaction, backupName);
        var epoch = "0001-01-01T00:00:00";

        string Col(string name) =>
            availableCols.Contains(name) ? $"\"{name}\"" : $"NULL AS \"{name}\"";

        string ColOrDefault(string name, string defaultValue) =>
            availableCols.Contains(name)
                ? $"COALESCE(\"{name}\", '{defaultValue}') AS \"{name}\""
                : $"'{defaultValue}' AS \"{name}\"";

        using (var readCmd = connection.CreateCommand())
        {
            readCmd.Transaction = transaction;
            readCmd.CommandText =
                $"""
                SELECT "SessionId", {Col("ProfileId")}, {Col("Title")}, {Col("UserId")},
                       {Col("ClientId")}, {ColOrDefault("Status", "0")},
                       {ColOrDefault("CreatedUtc", epoch)}, {ColOrDefault("LastActivityUtc", epoch)},
                       "Payload"
                FROM "{backupName}";
                """;

            using var reader = await readCmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var docId = await InsertDocumentAsync(connection, transaction, documentsTableName,
                    sessionType, reader.GetString(8));

                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText =
                    $"""
                    INSERT INTO "{tableName}" ("DocumentId","SessionId","ProfileId","Title","UserId","ClientId","Status","CreatedUtc","LastActivityUtc")
                    VALUES (@d,@si,@pi,@t,@ui,@ci,@st,@cu,@la);
                    """;

                AddParam(insertCmd, "@d", docId);
                AddParam(insertCmd, "@si", reader, 0);
                AddParam(insertCmd, "@pi", reader, 1);
                AddParam(insertCmd, "@t", reader, 2);
                AddParam(insertCmd, "@ui", reader, 3);
                AddParam(insertCmd, "@ci", reader, 4);
                AddParam(insertCmd, "@st", reader, 5);
                AddParam(insertCmd, "@cu", reader, 6);
                AddParam(insertCmd, "@la", reader, 7);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        await ExecuteNonQueryAsync(connection, transaction, $"""DROP TABLE "{backupName}";""");

        var sessionIdIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessions_SessionId");
        var profileIdIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessions_ProfileId");
        var lastActivityIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessions_LastActivityUtc");

        await ExecuteNonQueryAsync(connection, transaction,
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{sessionIdIndexName}" ON "{tableName}" ("SessionId");
            CREATE INDEX IF NOT EXISTS "{profileIdIndexName}" ON "{tableName}" ("ProfileId");
            CREATE INDEX IF NOT EXISTS "{lastActivityIndexName}" ON "{tableName}" ("LastActivityUtc");
            """);
    }

    [Obsolete("Schema migration from pre-v1 layout. Will be removed before v1.0.0 ships.")]
    private static async Task MigrateAIChatSessionEventsAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        string documentsTableName,
        string tablePrefix)
    {
        var backupName = $"{tableName}_v0";

        await ExecuteNonQueryAsync(connection, transaction,
            $"""ALTER TABLE "{tableName}" RENAME TO "{backupName}";""");

        await ExecuteNonQueryAsync(connection, transaction,
            $"""
            CREATE TABLE "{tableName}" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "DocumentId" INTEGER NOT NULL,
                "SessionId" TEXT NOT NULL,
                "ProfileId" TEXT NULL,
                "SessionStartedUtc" TEXT NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                FOREIGN KEY ("DocumentId") REFERENCES "{documentsTableName}" ("Id")
            );
            """);

        var eventType = typeof(CrestApps.Core.AI.Models.AIChatSessionEvent).FullName!;

        using (var readCmd = connection.CreateCommand())
        {
            readCmd.Transaction = transaction;
            readCmd.CommandText = $"""SELECT "SessionId","ProfileId","SessionStartedUtc","CreatedUtc","Payload" FROM "{backupName}";""";

            using var reader = await readCmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var docId = await InsertDocumentAsync(connection, transaction, documentsTableName,
                    eventType, reader.GetString(4));

                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText =
                    $"""
                    INSERT INTO "{tableName}" ("DocumentId","SessionId","ProfileId","SessionStartedUtc","CreatedUtc")
                    VALUES (@d,@si,@pi,@ss,@cu);
                    """;

                AddParam(insertCmd, "@d", docId);
                AddParam(insertCmd, "@si", reader, 0);
                AddParam(insertCmd, "@pi", reader, 1);
                AddParam(insertCmd, "@ss", reader, 2);
                AddParam(insertCmd, "@cu", reader, 3);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        await ExecuteNonQueryAsync(connection, transaction, $"""DROP TABLE "{backupName}";""");

        var sessionIdIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionEvents_SessionId");
        var profileIdIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionEvents_ProfileId");
        var sessionStartedIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionEvents_SessionStartedUtc");
        var createdUtcIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionEvents_CreatedUtc");

        await ExecuteNonQueryAsync(connection, transaction,
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{sessionIdIndexName}" ON "{tableName}" ("SessionId");
            CREATE INDEX IF NOT EXISTS "{profileIdIndexName}" ON "{tableName}" ("ProfileId");
            CREATE INDEX IF NOT EXISTS "{sessionStartedIndexName}" ON "{tableName}" ("SessionStartedUtc");
            CREATE INDEX IF NOT EXISTS "{createdUtcIndexName}" ON "{tableName}" ("CreatedUtc");
            """);
    }

    [Obsolete("Schema migration from pre-v1 layout. Will be removed before v1.0.0 ships.")]
    private static async Task MigrateAICompletionUsageAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        string documentsTableName,
        string tablePrefix)
    {
        var backupName = $"{tableName}_v0";

        await ExecuteNonQueryAsync(connection, transaction,
            $"""ALTER TABLE "{tableName}" RENAME TO "{backupName}";""");

        await ExecuteNonQueryAsync(connection, transaction,
            $"""
            CREATE TABLE "{tableName}" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "DocumentId" INTEGER NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                "SessionId" TEXT NULL,
                "InteractionId" TEXT NULL,
                FOREIGN KEY ("DocumentId") REFERENCES "{documentsTableName}" ("Id")
            );
            """);

        var usageType = typeof(CrestApps.Core.AI.Models.AICompletionUsageRecord).FullName!;

        using (var readCmd = connection.CreateCommand())
        {
            readCmd.Transaction = transaction;
            readCmd.CommandText = $"""SELECT "CreatedUtc","SessionId","InteractionId","Payload" FROM "{backupName}";""";

            using var reader = await readCmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var docId = await InsertDocumentAsync(connection, transaction, documentsTableName,
                    usageType, reader.GetString(3));

                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText =
                    $"""
                    INSERT INTO "{tableName}" ("DocumentId","CreatedUtc","SessionId","InteractionId")
                    VALUES (@d,@cu,@si,@ii);
                    """;

                AddParam(insertCmd, "@d", docId);
                AddParam(insertCmd, "@cu", reader, 0);
                AddParam(insertCmd, "@si", reader, 1);
                AddParam(insertCmd, "@ii", reader, 2);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        await ExecuteNonQueryAsync(connection, transaction, $"""DROP TABLE "{backupName}";""");

        var createdUtcIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AICompletionUsage_CreatedUtc");
        var sessionIdIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AICompletionUsage_SessionId");
        var interactionIdIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AICompletionUsage_InteractionId");

        await ExecuteNonQueryAsync(connection, transaction,
            $"""
            CREATE INDEX IF NOT EXISTS "{createdUtcIndexName}" ON "{tableName}" ("CreatedUtc");
            CREATE INDEX IF NOT EXISTS "{sessionIdIndexName}" ON "{tableName}" ("SessionId");
            CREATE INDEX IF NOT EXISTS "{interactionIdIndexName}" ON "{tableName}" ("InteractionId");
            """);
    }

    [Obsolete("Schema migration from pre-v1 layout. Will be removed before v1.0.0 ships.")]
    private static async Task MigrateAIChatSessionExtractedDataAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        string documentsTableName,
        string tablePrefix)
    {
        var backupName = $"{tableName}_v0";

        await ExecuteNonQueryAsync(connection, transaction,
            $"""ALTER TABLE "{tableName}" RENAME TO "{backupName}";""");

        await ExecuteNonQueryAsync(connection, transaction,
            $"""
            CREATE TABLE "{tableName}" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "DocumentId" INTEGER NOT NULL,
                "SessionId" TEXT NOT NULL,
                "ProfileId" TEXT NOT NULL,
                "SessionStartedUtc" TEXT NOT NULL,
                "SessionEndedUtc" TEXT NULL,
                "UpdatedUtc" TEXT NOT NULL,
                FOREIGN KEY ("DocumentId") REFERENCES "{documentsTableName}" ("Id")
            );
            """);

        var extractedType = typeof(CrestApps.Core.AI.Models.AIChatSessionExtractedDataRecord).FullName!;

        using (var readCmd = connection.CreateCommand())
        {
            readCmd.Transaction = transaction;
            readCmd.CommandText = $"""SELECT "SessionId","ProfileId","SessionStartedUtc","SessionEndedUtc","UpdatedUtc","Payload" FROM "{backupName}";""";

            using var reader = await readCmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var docId = await InsertDocumentAsync(connection, transaction, documentsTableName,
                    extractedType, reader.GetString(5));

                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText =
                    $"""
                    INSERT INTO "{tableName}" ("DocumentId","SessionId","ProfileId","SessionStartedUtc","SessionEndedUtc","UpdatedUtc")
                    VALUES (@d,@si,@pi,@ss,@se,@uu);
                    """;

                AddParam(insertCmd, "@d", docId);
                AddParam(insertCmd, "@si", reader, 0);
                AddParam(insertCmd, "@pi", reader, 1);
                AddParam(insertCmd, "@ss", reader, 2);
                AddParam(insertCmd, "@se", reader, 3);
                AddParam(insertCmd, "@uu", reader, 4);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        await ExecuteNonQueryAsync(connection, transaction, $"""DROP TABLE "{backupName}";""");

        var sessionIdIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionExtractedData_SessionId");
        var profileIdIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionExtractedData_ProfileId");
        var sessionStartedIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionExtractedData_SessionStartedUtc");
        var updatedUtcIndexName = GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionExtractedData_UpdatedUtc");

        await ExecuteNonQueryAsync(connection, transaction,
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{sessionIdIndexName}" ON "{tableName}" ("SessionId");
            CREATE INDEX IF NOT EXISTS "{profileIdIndexName}" ON "{tableName}" ("ProfileId");
            CREATE INDEX IF NOT EXISTS "{sessionStartedIndexName}" ON "{tableName}" ("SessionStartedUtc");
            CREATE INDEX IF NOT EXISTS "{updatedUtcIndexName}" ON "{tableName}" ("UpdatedUtc");
            """);
    }

    [Obsolete("Schema migration from pre-v1 layout. Will be removed before v1.0.0 ships.")]
    private static async Task CreateCatalogIndexes(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        string tablePrefix)
    {
        await ExecuteNonQueryAsync(connection, transaction,
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}CatalogRecords_EntityType_ItemId")}" ON "{tableName}" ("EntityType", "ItemId");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}CatalogRecords_EntityType_Name")}" ON "{tableName}" ("EntityType", "Name");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}CatalogRecords_EntityType_Source")}" ON "{tableName}" ("EntityType", "Source");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}CatalogRecords_EntityType_SessionId")}" ON "{tableName}" ("EntityType", "SessionId");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}CatalogRecords_EntityType_ChatInteractionId")}" ON "{tableName}" ("EntityType", "ChatInteractionId");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}CatalogRecords_EntityType_ReferenceId_ReferenceType")}" ON "{tableName}" ("EntityType", "ReferenceId", "ReferenceType");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}CatalogRecords_EntityType_AIDocumentId")}" ON "{tableName}" ("EntityType", "AIDocumentId");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}CatalogRecords_EntityType_UserId_Name")}" ON "{tableName}" ("EntityType", "UserId", "Name");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}CatalogRecords_EntityType_Type")}" ON "{tableName}" ("EntityType", "Type");
            """);
    }

    private static async Task EnsureOptionalTablesAsync(
        CrestAppsEntityDbContext dbContext,
        string tablePrefix)
    {
        var documentsTableName = GetSafeSqlIdentifier($"{tablePrefix}Documents");
        var eventsTableName = GetSafeSqlIdentifier($"{tablePrefix}AIChatSessionEvents");
        var usageTableName = GetSafeSqlIdentifier($"{tablePrefix}AICompletionUsage");
        var extractedTableName = GetSafeSqlIdentifier($"{tablePrefix}AIChatSessionExtractedData");

        string documentsSql =
            $"""
            CREATE TABLE IF NOT EXISTS "{documentsTableName}" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "Type" TEXT NOT NULL,
                "Content" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}Documents_Type")}" ON "{documentsTableName}" ("Type");
            """;

        await dbContext.Database.ExecuteSqlRawAsync(documentsSql);

        string eventsSql =
            $"""
            CREATE TABLE IF NOT EXISTS "{eventsTableName}" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "DocumentId" INTEGER NOT NULL,
                "SessionId" TEXT NOT NULL,
                "ProfileId" TEXT NULL,
                "SessionStartedUtc" TEXT NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                FOREIGN KEY ("DocumentId") REFERENCES "{documentsTableName}" ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionEvents_SessionId")}" ON "{eventsTableName}" ("SessionId");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionEvents_ProfileId")}" ON "{eventsTableName}" ("ProfileId");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionEvents_SessionStartedUtc")}" ON "{eventsTableName}" ("SessionStartedUtc");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionEvents_CreatedUtc")}" ON "{eventsTableName}" ("CreatedUtc");
            """;

        await dbContext.Database.ExecuteSqlRawAsync(eventsSql);

        string usageSql =
            $"""
            CREATE TABLE IF NOT EXISTS "{usageTableName}" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "DocumentId" INTEGER NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                "SessionId" TEXT NULL,
                "InteractionId" TEXT NULL,
                FOREIGN KEY ("DocumentId") REFERENCES "{documentsTableName}" ("Id")
            );
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AICompletionUsage_CreatedUtc")}" ON "{usageTableName}" ("CreatedUtc");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AICompletionUsage_SessionId")}" ON "{usageTableName}" ("SessionId");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AICompletionUsage_InteractionId")}" ON "{usageTableName}" ("InteractionId");
            """;

        await dbContext.Database.ExecuteSqlRawAsync(usageSql);

        string extractedSql =
            $"""
            CREATE TABLE IF NOT EXISTS "{extractedTableName}" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "DocumentId" INTEGER NOT NULL,
                "SessionId" TEXT NOT NULL,
                "ProfileId" TEXT NOT NULL,
                "SessionStartedUtc" TEXT NOT NULL,
                "SessionEndedUtc" TEXT NULL,
                "UpdatedUtc" TEXT NOT NULL,
                FOREIGN KEY ("DocumentId") REFERENCES "{documentsTableName}" ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionExtractedData_SessionId")}" ON "{extractedTableName}" ("SessionId");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionExtractedData_ProfileId")}" ON "{extractedTableName}" ("ProfileId");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionExtractedData_SessionStartedUtc")}" ON "{extractedTableName}" ("SessionStartedUtc");
            CREATE INDEX IF NOT EXISTS "{GetSafeSqlIdentifier($"IX_{tablePrefix}AIChatSessionExtractedData_UpdatedUtc")}" ON "{extractedTableName}" ("UpdatedUtc");
            """;

        await dbContext.Database.ExecuteSqlRawAsync(extractedSql);
    }

    private static async Task<bool> HasColumnAsync(
        CrestAppsEntityDbContext dbContext,
        string tableName,
        string columnName)
    {
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""PRAGMA table_info("{tableName}");""";

        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> TableExistsAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"""SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;""";

        var param = cmd.CreateParameter();
        param.ParameterName = "@name";
        param.Value = tableName;
        cmd.Parameters.Add(param);

        var result = await cmd.ExecuteScalarAsync();

        return Convert.ToInt64(result) > 0;
    }

    private static async Task<long> InsertDocumentAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string documentsTableName,
        string type,
        string content)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"""INSERT INTO "{documentsTableName}" ("Type","Content") VALUES (@t,@c); SELECT last_insert_rowid();""";

        var typeParam = cmd.CreateParameter();
        typeParam.ParameterName = "@t";
        typeParam.Value = type;
        cmd.Parameters.Add(typeParam);

        var contentParam = cmd.CreateParameter();
        contentParam.ParameterName = "@c";
        contentParam.Value = content;
        cmd.Parameters.Add(contentParam);

        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteNonQueryAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParam(
        System.Data.Common.DbCommand command,
        string name,
        long value)
    {
        var param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        command.Parameters.Add(param);
    }

    private static void AddParam(
        System.Data.Common.DbCommand command,
        string name,
        System.Data.Common.DbDataReader reader,
        int ordinal)
    {
        var param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = reader.IsDBNull(ordinal) ? DBNull.Value : reader.GetValue(ordinal);
        command.Parameters.Add(param);
    }

    /// <summary>
    /// Gets the set of column names present in the specified table.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="transaction">The active transaction.</param>
    /// <param name="tableName">The sanitized table name to inspect.</param>
    /// <returns>A case-insensitive set of column names.</returns>
    private static async Task<HashSet<string>> GetColumnNamesAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"""PRAGMA table_info("{tableName}");""";

        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static string GetSafeSqlIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        if (identifier.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException($"Unsupported SQLite identifier '{identifier}'.");
        }

        return identifier;
    }
}
