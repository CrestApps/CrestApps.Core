using System.Data.Common;
using System.Text.Json;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Copilot;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Mcp.Models;
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
using CrestApps.Core.Mvc.Web.Areas.Admin.Indexes;
using CrestApps.Core.Mvc.Web.Areas.AI.Handlers;
using CrestApps.Core.Mvc.Web.Areas.AI.Services;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Services;
using CrestApps.Core.Mvc.Web.Areas.Indexing.Services;
using CrestApps.Core.Services;
using CrestApps.Core.Startup.Shared.Handlers;
using CrestApps.Core.Startup.Shared.Models;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Indexes;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace CrestApps.Core.Mvc.Web.Services;

internal static class YesSqlServiceCollectionExtensions
{
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
            .AddScoped<ICatalogEntryHandler<AIMemoryEntry>, AIMemoryEntryHandler>()
            .AddScoped<IAuthorizationHandler, SampleChatInteractionDocumentAuthorizationHandler>()
            .AddScoped<IAuthorizationHandler, SampleAIChatSessionDocumentAuthorizationHandler>()
            .AddScoped<IAIChatDocumentEventHandler, SampleAIChatDocumentEventHandler>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndexProvider, ArticleIndexProvider>());

        services
            .AddYesSqlDocumentCatalog<Article, ArticleIndex>()
            .AddScoped<ICatalogEntryHandler<Article>, ArticleHandler>()
            .AddSharedArticleServices()
            .AddSharedTemplateProviders()
            .AddKeyedScoped<IAIReferenceLinkResolver, ArticleAIReferenceLinkResolver>(IndexProfileTypes.Articles)
            .AddScoped<SampleCitationReferenceCollector>()
            .AddScoped<CompositeAIReferenceLinkResolver>()
            .AddScoped<IAIDataSourceIndexingService, DefaultAIDataSourceIndexingService>()
            .AddScoped<ICopilotCredentialStore, JsonFileCopilotCredentialStore>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, DocumentChatInteractionSettingsHandler>());
        services.AddSingleton<IConfigureOptions<A2AHostOptions>, SiteSettingsConfigureStoredOptions<A2AHostOptions>>();
        services.AddSingleton<IConfigureOptions<McpServerOptions>, SiteSettingsConfigureStoredOptions<McpServerOptions>>();
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
        await TryCreateTableAsync(() => schemaBuilder.CreateMapIndexTableAsync<ArticleIndex>(t => t
            .Column<string>(nameof(ArticleIndex.ItemId), c => c.WithLength(26))
            .Column<string>(nameof(ArticleIndex.Title), c => c.WithLength(255))));
        await transaction.CommitAsync();

        await MigrateLegacyExtractedDataRecordsAsync(services, storeOptions, logger);
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
        command.CommandText = "PRAGMA synchronous=FULL;";
        await command.ExecuteNonQueryAsync();
        command.CommandText = "PRAGMA busy_timeout=30000;";
        await command.ExecuteNonQueryAsync();
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

    private static async Task MigrateLegacyExtractedDataRecordsAsync(
        IServiceProvider services,
        YesSqlStoreOptions storeOptions,
        ILogger logger)
    {
        if (string.Equals(storeOptions.AICollectionName, storeOptions.DefaultCollectionName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var legacyRows = await GetLegacyExtractedDataRowsAsync(services, storeOptions);
        if (legacyRows.Count == 0)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var extractedDataStore = scope.ServiceProvider.GetRequiredService<IAIChatSessionExtractedDataStore>();
        var committer = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();

        foreach (var record in legacyRows
                     .Select(row => row.Record)
                     .Where(record => record is not null)
                     .GroupBy(record => record.SessionId, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group
                         .OrderByDescending(record => record.UpdatedUtc)
                         .First()))
        {
            await extractedDataStore.SaveAsync(record);
        }

        await committer.CommitAsync();
        await DeleteLegacyExtractedDataRowsAsync(services, storeOptions, legacyRows.Select(row => row.Id).ToList());

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Migrated {Count} legacy AI chat extracted-data snapshot records into the AI collection.", legacyRows.Count);
        }
    }

    private static async Task<List<LegacyExtractedDataRow>> GetLegacyExtractedDataRowsAsync(
        IServiceProvider services,
        YesSqlStoreOptions storeOptions)
    {
        var rows = new List<LegacyExtractedDataRow>();
        var tableName = GetDefaultCollectionDocumentTableName(storeOptions);

        await using var connection = services.GetRequiredService<IStore>().Configuration.ConnectionFactory.CreateConnection();
        await connection.OpenAsync();
        await ConfigureSqliteConnectionAsync(connection);

        if (!await TableExistsAsync(connection, tableName))
        {
            return rows;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id, Content FROM [{tableName}] WHERE Type = @type";
        AddParameter(command, "@type", "CrestApps.Core.AI.Models.AIChatSessionExtractedDataRecord, CrestApps.Core.AI.Abstractions");

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            var record = JsonSerializer.Deserialize<AIChatSessionExtractedDataRecord>(reader.GetString(1));
            if (record is null || string.IsNullOrWhiteSpace(record.SessionId) || string.IsNullOrWhiteSpace(record.ProfileId))
            {
                continue;
            }

            rows.Add(new LegacyExtractedDataRow(reader.GetInt32(0), record));
        }

        return rows;
    }

    private static async Task DeleteLegacyExtractedDataRowsAsync(
        IServiceProvider services,
        YesSqlStoreOptions storeOptions,
        List<int> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var tableName = GetDefaultCollectionDocumentTableName(storeOptions);

        await using var connection = services.GetRequiredService<IStore>().Configuration.ConnectionFactory.CreateConnection();
        await connection.OpenAsync();
        await ConfigureSqliteConnectionAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var id in ids.Distinct())
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM [{tableName}] WHERE Id = @id";
            AddParameter(command, "@id", id);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
        AddParameter(command, "@name", tableName);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static string GetDefaultCollectionDocumentTableName(YesSqlStoreOptions storeOptions)
    {
        return string.IsNullOrWhiteSpace(storeOptions.DefaultCollectionName)
            ? "Document"
            : $"{storeOptions.DefaultCollectionName}_Document";
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record LegacyExtractedDataRow(int Id, AIChatSessionExtractedDataRecord Record);
}
