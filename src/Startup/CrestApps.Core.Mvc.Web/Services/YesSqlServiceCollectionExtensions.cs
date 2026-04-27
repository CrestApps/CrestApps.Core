using System.Data.Common;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Copilot;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.A2A;
using CrestApps.Core.Data.YesSql.Indexes.AI;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using CrestApps.Core.Data.YesSql.Indexes.AIMemory;
using CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;
using CrestApps.Core.Data.YesSql.Indexes.DataSources;
using CrestApps.Core.Data.YesSql.Indexes.Indexing;
using CrestApps.Core.Data.YesSql.Indexes.Mcp;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Mvc.Web.Areas.Admin.Handlers;
using CrestApps.Core.Mvc.Web.Areas.Admin.Indexes;
using CrestApps.Core.Mvc.Web.Areas.AI.Handlers;
using CrestApps.Core.Mvc.Web.Areas.AI.Services;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Handlers;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Services;
using CrestApps.Core.Mvc.Web.Areas.Indexing.Services;
using CrestApps.Core.Services;
using CrestApps.Core.Startup.Shared.Models;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace CrestApps.Core.Mvc.Web.Services;

internal static class YesSqlServiceCollectionExtensions
{
    private static readonly string LegacyArticleDocumentType = $"{typeof(global::CrestApps.Core.Mvc.Web.Areas.Admin.Models.Article).FullName}, {typeof(global::CrestApps.Core.Mvc.Web.Areas.Admin.Models.Article).Assembly.GetName().Name}";
    private static readonly string CurrentArticleDocumentType = $"{typeof(Article).FullName}, {typeof(Article).Assembly.GetName().Name}";

    /// <summary>
    /// Registers the MVC sample host services that sit around the framework:
    /// YesSql storage, sample-only managers, article demo services, and the
    /// provider-specific option bridges used by the admin UI.
    /// </summary>
    public static IServiceCollection AddMvcSampleHostServices(this IServiceCollection services, string appDataPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(appDataPath);

        var dbPath = Path.Combine(appDataPath, "crestapps.db");

        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 30,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        };

        Data.YesSql.ServiceCollectionExtensions.AddCoreYesSqlDataStore(services, configuration => configuration
            .UseSqLite(connectionStringBuilder.ToString())
        );

        services
            .AddScoped<AIProfileDocumentService>()
            .AddScoped<AIProfileTemplateDocumentService>();

        services
            .AddScoped<SampleAIChatSessionEventService>()
            .AddScoped<SampleAICompletionUsageService>()
            .AddScoped<SampleAIChatSessionEventPostCloseObserver>()
            .AddScoped<SampleAIChatSessionExtractedDataService>()
            .AddScoped<IAICompletionUsageObserver>(sp => sp.GetRequiredService<SampleAICompletionUsageService>())
            .AddScoped<IAIChatSessionAnalyticsRecorder>(sp => sp.GetRequiredService<SampleAIChatSessionEventPostCloseObserver>())
            .AddScoped<IAIChatSessionConversionGoalRecorder>(sp => sp.GetRequiredService<SampleAIChatSessionEventPostCloseObserver>())
            .AddScoped<IAIChatSessionExtractedDataRecorder>(sp => sp.GetRequiredService<SampleAIChatSessionExtractedDataService>())
            .AddScoped<IAIChatSessionHandler, AnalyticsChatSessionHandler>();

        services
            .AddScoped<ICatalogEntryHandler<AIMemoryEntry>, AIMemoryEntryIndexingHandler>()
            .AddScoped<SampleAIDocumentIndexingService>()
            .AddScoped<IAuthorizationHandler, SampleChatInteractionDocumentAuthorizationHandler>()
            .AddScoped<IAuthorizationHandler, SampleAIChatSessionDocumentAuthorizationHandler>()
            .AddScoped<IAIChatDocumentEventHandler, SampleAIChatDocumentEventHandler>();

        services
            .AddYesSqlDocumentCatalog<Article, ArticleIndex>()
            .AddScoped<ICatalogEntryHandler<Article>, ArticleIndexingHandler>()
            .AddSharedArticleServices()
            .AddSharedTemplateProviders()
            .AddKeyedScoped<IAIReferenceLinkResolver, ArticleAIReferenceLinkResolver>(IndexProfileTypes.Articles)
            .AddScoped<SampleCitationReferenceCollector>()
            .AddScoped<CompositeAIReferenceLinkResolver>()
            .AddScoped<IAIDataSourceIndexingService, DefaultAIDataSourceIndexingService>()
            .AddScoped<ICopilotCredentialStore, JsonFileCopilotCredentialStore>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, DocumentChatInteractionSettingsHandler>());
        services.ConfigureOptions<SampleCopilotOptionsConfiguration>();
        services.ConfigureOptions<SampleClaudeOptionsConfiguration>();
        services.Configure<IndexProfileSourceOptions>(options => options
            .AddOrUpdate(ElasticsearchConstants.ProviderName, "Elasticsearch", IndexProfileTypes.Articles, descriptor =>
            {
                descriptor.DisplayName = "Articles";
                descriptor.Description = "Create an Elasticsearch index for sample article records managed in the MVC app.";
            })
        );
        services.Configure<IndexProfileSourceOptions>(options => options
            .AddOrUpdate(ElasticsearchConstants.ProviderName, "Azure AI Search", IndexProfileTypes.Articles, descriptor =>
            {
                descriptor.DisplayName = "Articles";
                descriptor.Description = "Create an Azure AI Search index for sample article records managed in the MVC app.";
            })
        );

        return services;
    }

    /// <summary>
    /// Creates YesSql index tables if they do not already exist.
    /// Call once at startup after <see cref="WebApplication.Build"/>.
    /// </summary>
    public static async Task InitializeYesSqlSchemaAsync(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var store = services.GetRequiredService<IStore>();
        var options = services.GetService<IOptions<AIChatSessionMetricsIndexSchemaOptions>>().Value;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CrestApps.Core.Mvc.Web.YesSql");
        var storeOptions = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<YesSqlStoreOptions>>().Value;
        RegisterIndexes(store);
        await InitializeCollectionsAsync(store, storeOptions);
        await using var connection = store.Configuration.ConnectionFactory.CreateConnection();
        await connection.OpenAsync();
        await ConfigureSqliteConnectionAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();
        var schemaBuilder = new SchemaBuilder(store.Configuration, transaction);
        await TryCreateTableAsync(() => schemaBuilder.CreateAIProfileIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIProviderConnectionIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateA2AConnectionIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateMcpConnectionIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateMcpPromptIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateMcpResourceIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIDeploymentIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIProfileTemplateIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIChatSessionIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIChatSessionMetricsSchemaAsync(storeOptions, options));
        await TryCreateTableAsync(() => schemaBuilder.CreateAICompletionUsageIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIChatSessionExtractedDataIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIChatSessionPromptIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIDocumentIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIDocumentChunkIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateSearchIndexProfileIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIDataSourceIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIMemoryEntryIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateChatInteractionIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateChatInteractionPromptIndexSchemaAsync(storeOptions));
        await MigrateLegacyArticleDocumentTypesAsync(store, connection, transaction, logger);
        await EnsureAIDocumentIndexExtensionColumnAsync(store, connection, transaction, schemaBuilder, storeOptions, logger);
        await TryCreateTableAsync(() => schemaBuilder.CreateMapIndexTableAsync<ArticleIndex>(t => t
            .Column<string>(nameof(ArticleIndex.ItemId), c => c.WithLength(26))
            .Column<string>(nameof(ArticleIndex.Title), c => c.WithLength(255))));
        await transaction.CommitAsync();
    }

    private static async Task InitializeCollectionsAsync(IStore store, YesSqlStoreOptions storeOptions)
    {
        var collections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(storeOptions.AICollectionName))
        {
            collections.Add(storeOptions.AICollectionName);
        }

        if (!string.IsNullOrWhiteSpace(storeOptions.AIDocsCollectionName))
        {
            collections.Add(storeOptions.AIDocsCollectionName);
        }

        if (!string.IsNullOrWhiteSpace(storeOptions.AIMemoryCollectionName))
        {
            collections.Add(storeOptions.AIMemoryCollectionName);
        }

        foreach (var collection in collections)
        {
            await store.InitializeCollectionAsync(collection);
        }
    }

    private static async Task ConfigureSqliteConnectionAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        _ = await command.ExecuteScalarAsync();
        command.CommandText = "PRAGMA synchronous=NORMAL;";
        await command.ExecuteNonQueryAsync();
        command.CommandText = "PRAGMA busy_timeout=30000;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureAIDocumentIndexExtensionColumnAsync(IStore store, DbConnection connection, DbTransaction transaction, SchemaBuilder schemaBuilder, YesSqlStoreOptions storeOptions, ILogger logger)
    {
        var tableName = await FindIndexTableNameAsync(connection, transaction, store.Configuration.TablePrefix, nameof(AIDocumentIndex));
        if (await ColumnExistsAsync(connection, transaction, tableName, nameof(AIDocumentIndex.Extension)))
        {
            return;
        }

        await schemaBuilder.AddAIDocumentIndexExtensionColumnAsync(storeOptions);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Added missing column '{ColumnName}' to YesSql index table '{TableName}'.", nameof(AIDocumentIndex.Extension), tableName);
        }
    }

    private static async Task MigrateLegacyArticleDocumentTypesAsync(IStore store, DbConnection connection, DbTransaction transaction, ILogger logger)
    {
        var tableName = $"{store.Configuration.TablePrefix}Document";
        if (!await TableExistsAsync(connection, transaction, tableName))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE [{tableName}]
            SET [Type] = $currentType
            WHERE [Type] = $legacyType
            """;

        var currentTypeParameter = command.CreateParameter();
        currentTypeParameter.ParameterName = "$currentType";
        currentTypeParameter.Value = CurrentArticleDocumentType;
        command.Parameters.Add(currentTypeParameter);

        var legacyTypeParameter = command.CreateParameter();
        legacyTypeParameter.ParameterName = "$legacyType";
        legacyTypeParameter.Value = LegacyArticleDocumentType;
        command.Parameters.Add(legacyTypeParameter);

        var updatedCount = await command.ExecuteNonQueryAsync();
        if (updatedCount <= 0 || !logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        logger.LogInformation(
            "Migrated {Count} persisted article documents from legacy type '{LegacyType}' to '{CurrentType}'.",
            updatedCount,
            LegacyArticleDocumentType,
            CurrentArticleDocumentType);
    }

    private static async Task<string> FindIndexTableNameAsync(DbConnection connection, DbTransaction transaction, string tablePrefix, string indexTypeName)
    {
        var expectedName = $"{tablePrefix}{indexTypeName}";
        var pattern = $"{tablePrefix}%{indexTypeName}%";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table' AND (name = $expectedName OR name LIKE $pattern)
            ORDER BY CASE WHEN name = $expectedName THEN 0 ELSE 1 END, name
            LIMIT 1
            """;

        var expectedNameParameter = command.CreateParameter();
        expectedNameParameter.ParameterName = "$expectedName";
        expectedNameParameter.Value = expectedName;
        command.Parameters.Add(expectedNameParameter);

        var patternParameter = command.CreateParameter();
        patternParameter.ParameterName = "$pattern";
        patternParameter.Value = pattern;
        command.Parameters.Add(patternParameter);

        var result = await command.ExecuteScalarAsync();

        return result as string ?? expectedName;
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection connection, DbTransaction transaction, string tableName, string columnName)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info('{tableName.Replace("'", "''")}');";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, DbTransaction transaction, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = $tableName
            LIMIT 1
            """;

        var tableNameParameter = command.CreateParameter();
        tableNameParameter.ParameterName = "$tableName";
        tableNameParameter.Value = tableName;
        command.Parameters.Add(tableNameParameter);

        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task TryCreateTableAsync(Func<Task> createTable)
    {
        try
        {
            await createTable();
        }
        catch
        { /* Table already exists. */
        }
    }

    private static void RegisterIndexes(IStore store)
    {
        // Host-specific index provider. Shared index providers are registered
        // automatically via DI in the per-feature AddCoreAI*StoresYesSql() methods.
        store.RegisterIndexes<ArticleIndexProvider>();
    }

}
