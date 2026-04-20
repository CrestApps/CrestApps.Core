using System.Data.Common;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Indexing;
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
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Mvc.Web.Areas.Admin.Handlers;
using CrestApps.Core.Mvc.Web.Areas.Admin.Indexes;
using CrestApps.Core.Mvc.Web.Areas.Admin.Models;
using CrestApps.Core.Mvc.Web.Areas.Admin.Services;
using CrestApps.Core.Mvc.Web.Areas.AI.Handlers;
using CrestApps.Core.Mvc.Web.Areas.AI.Services;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Handlers;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Services;
using CrestApps.Core.Mvc.Web.Areas.DataSources.Handlers;
using CrestApps.Core.Mvc.Web.Areas.Indexing.Services;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using YesSql;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace CrestApps.Core.Mvc.Web.Services;

internal static class YesSqlServiceCollectionExtensions
{
    private static readonly (string LegacyValue, string CurrentValue)[] _legacyDocumentTypeReplacements = [("CrestApps.AI.", "CrestApps.Core.AI."), ("CrestApps.Infrastructure.", "CrestApps.Core.Infrastructure."), ("CrestApps.Mvc.Web", "CrestApps.Core.Mvc.Web"),];

    /// <summary>
    /// Registers YesSql with SQLite, all index providers, and the catalog/manager
    /// services that the MVC sample application needs. Call this from Program.cs to
    /// keep the data-store wiring in one place.
    /// </summary>
    public static IServiceCollection AddCoreYesSqlDataStore(this IServiceCollection services, string appDataPath)
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

        services.Configure<YesSqlStoreOptions>(options =>
        {
            // The MVC sample host keeps all documents in a single SQLite database
            // with no collection separation. Override the defaults so that all stores
            // and index providers use the root (null) collection.
            options.AICollectionName = null;
            options.AIMemoryCollectionName = null;
            options.AIDocsCollectionName = null;
        });

        Data.YesSql.ServiceCollectionExtensions.AddCoreYesSqlDataStore(services, configuration => configuration
            .UseSqLite(connectionStringBuilder.ToString())
            .SetTablePrefix("CA_")
        );

        // Host-specific managers.
        services
            .AddScoped<DefaultAIProfileTemplateManager>()
            .AddScoped<IAIProfileTemplateManager>(sp => sp.GetRequiredService<DefaultAIProfileTemplateManager>())
            .AddScoped<INamedSourceCatalogManager<AIProfileTemplate>>(sp => sp.GetRequiredService<DefaultAIProfileTemplateManager>())
            .AddScoped<INamedCatalogManager<AIProfileTemplate>>(sp => sp.GetRequiredService<DefaultAIProfileTemplateManager>())
            .AddScoped<DefaultAIDeploymentManager>()
            .AddScoped<IAIDeploymentManager>(sp => sp.GetRequiredService<DefaultAIDeploymentManager>())
            .AddScoped<INamedSourceCatalogManager<AIDeployment>>(sp => sp.GetRequiredService<DefaultAIDeploymentManager>())
            .AddScoped<IAIProfileManager, SimpleAIProfileManager>()
            .AddScoped<AIProfileDocumentService>()
            .AddScoped<AIProfileTemplateDocumentService>();

        // Host-specific chat session services.
        services
            .AddScoped<MvcAIChatSessionEventService>()
            .AddScoped<MvcAICompletionUsageService>()
            .AddScoped<MvcAIChatSessionEventPostCloseObserver>()
            .AddScoped<MvcAIChatSessionExtractedDataService>()
            .AddScoped<IAICompletionUsageObserver>(sp => sp.GetRequiredService<MvcAICompletionUsageService>())
            .AddScoped<IAIChatSessionAnalyticsRecorder>(sp => sp.GetRequiredService<MvcAIChatSessionEventPostCloseObserver>())
            .AddScoped<IAIChatSessionConversionGoalRecorder>(sp => sp.GetRequiredService<MvcAIChatSessionEventPostCloseObserver>())
            .AddScoped<IAIChatSessionExtractedDataRecorder>(sp => sp.GetRequiredService<MvcAIChatSessionExtractedDataService>())
            .AddScoped<IAIChatSessionHandler, AnalyticsChatSessionHandler>();

        // Host-specific indexing and authorization services.
        services
            .AddScoped<ICatalogEntryHandler<AIMemoryEntry>, AIMemoryEntryIndexingHandler>()
            .AddScoped<MvcAIDocumentIndexingService>()
            .AddScoped<ISearchIndexProfileManager, SearchIndexProfileManager>()
            .AddScoped<IAuthorizationHandler, MvcChatInteractionDocumentAuthorizationHandler>()
            .AddScoped<IAuthorizationHandler, MvcAIChatSessionDocumentAuthorizationHandler>()
            .AddScoped<IAIChatDocumentEventHandler, MvcAIChatDocumentEventHandler>();

        // Host-specific data-source and article services.
        services
            .AddYesSqlDocumentCatalog<Article, ArticleIndex>()
            .AddScoped<ICatalogEntryHandler<AIDataSource>, AIDataSourceIndexingHandler>()
            .AddScoped<ICatalogEntryHandler<Article>, ArticleIndexingHandler>()
            .AddScoped<ArticleIndexingService>();

        return services;
    }

    /// <summary>
    /// Creates YesSql index tables if they do not already exist.
    /// Call once at startup after <see cref = "WebApplication.Build"/>.
    /// </summary>
    public static async Task InitializeYesSqlSchemaAsync(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var store = services.GetRequiredService<IStore>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CrestApps.Core.Mvc.Web.YesSql");
        var storeOptions = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<YesSqlStoreOptions>>().Value;
        RegisterIndexes(store);
        await using var connection = store.Configuration.ConnectionFactory.CreateConnection();
        await connection.OpenAsync();
        await ConfigureSqliteConnectionAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();
        var schemaBuilder = new SchemaBuilder(store.Configuration, transaction);
        await NormalizeLegacyDocumentTypeNamesAsync(store, connection, transaction, logger);
        await TryCreateTableAsync(() => schemaBuilder.CreateAIProfileIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIProviderConnectionIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateA2AConnectionIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateMcpConnectionIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateMcpPromptIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateMcpResourceIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIDeploymentIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIProfileTemplateIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIChatSessionIndexSchemaAsync(storeOptions));
        await TryCreateTableAsync(() => schemaBuilder.CreateAIChatSessionMetricsSchemaAsync(storeOptions));
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
        await EnsureAIDocumentIndexExtensionColumnAsync(store, connection, transaction, schemaBuilder, storeOptions, logger);
        await TryCreateTableAsync(() => schemaBuilder.CreateMapIndexTableAsync<ArticleIndex>(t => t
            .Column<string>(nameof(ArticleIndex.ItemId), c => c.WithLength(26))
            .Column<string>(nameof(ArticleIndex.Title), c => c.WithLength(255))));
        await transaction.CommitAsync();
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

    private static async Task NormalizeLegacyDocumentTypeNamesAsync(IStore store, DbConnection connection, DbTransaction transaction, ILogger logger)
    {
        var dialect = store.Configuration.SqlDialect;
        var documentTableName = store.Configuration.TableNameConvention.GetDocumentTable(string.Empty);
        var table = $"{store.Configuration.TablePrefix}{documentTableName}";
        var quotedTableName = dialect.QuoteForTableName(table, store.Configuration.Schema);
        var quotedTypeColumnName = dialect.QuoteForColumnName(nameof(Document.Type));
        foreach (var (legacyValue, currentValue) in _legacyDocumentTypeReplacements)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE {quotedTableName}
                SET {quotedTypeColumnName} = REPLACE({quotedTypeColumnName}, '{legacyValue}', '{currentValue}')
                WHERE {quotedTypeColumnName} LIKE '%{legacyValue}%'
                """;
            var updated = await command.ExecuteNonQueryAsync();
            if (updated > 0 && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Updated {Count} stored YesSql document type names in {TableName} from '{LegacyValue}' to '{CurrentValue}'.", updated, table, legacyValue, currentValue);
            }
        }
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

    /// <summary>
    /// Seeds the database with sample articles on first run. Subsequent runs
    /// skip seeding because articles already exist.
    /// </summary>
    public static async Task SeedArticlesAsync(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalog<Article>>();
        var existing = await catalog.GetAllAsync();
        if (existing.Count > 0)
        {
            return;
        }

        var articles = new[]
        {
            new Article
            {
                ItemId = UniqueId.GenerateId(),
                Title = "What Are Large Language Models?",
                CreatedUtc = DateTime.UtcNow,
                Description = """
                    # What Are Large Language Models?

                    Large Language Models (LLMs) are deep learning models trained on vast corpora of text data. They learn statistical patterns in language and can generate coherent, context-aware text.

                    ## Key Characteristics

                    - **Scale**: Billions of parameters trained on terabytes of text.
                    - **Generalization**: Capable of performing many tasks without task-specific training.
                    - **Context Window**: The amount of text the model can consider at once.

                    ## Common Use Cases

                    1. Conversational AI and chatbots
                    2. Content generation and summarization
                    3. Code assistance and generation
                    4. Translation and language understanding

                    LLMs form the backbone of modern AI assistants and are the foundation for tools like GitHub Copilot.
                    """,
            },
            new Article
            {
                ItemId = UniqueId.GenerateId(),
                Title = "Understanding Embeddings and Vector Search",
                CreatedUtc = DateTime.UtcNow,
                Description = """
                    # Understanding Embeddings and Vector Search

                    Embeddings are numerical representations of text (or other data) in a high-dimensional vector space. Similar concepts end up close together in this space.

                    ## How Embeddings Work

                    An embedding model converts text into a fixed-length array of floating-point numbers. For example, the sentence "The cat sat on the mat" might become a 1536-dimensional vector.

                    ## Vector Search

                    Vector search (also called semantic search) finds documents whose embeddings are closest to a query embedding using distance metrics like **cosine similarity** or **dot product**.

                    ### Why It Matters

                    - Traditional keyword search misses synonyms and paraphrases.
                    - Vector search understands meaning, not just exact words.
                    - Combining both approaches (hybrid search) gives the best results.

                    ## Providers

                    Popular embedding providers include OpenAI (`text-embedding-3-small`), Azure OpenAI, and open-source models like Sentence Transformers.
                    """,
            },
            new Article
            {
                ItemId = UniqueId.GenerateId(),
                Title = "Retrieval-Augmented Generation (RAG) Explained",
                CreatedUtc = DateTime.UtcNow,
                Description = """
                    # Retrieval-Augmented Generation (RAG) Explained

                    RAG is an architecture that combines information retrieval with text generation. Instead of relying solely on the model's training data, RAG retrieves relevant documents at query time and includes them in the prompt.

                    ## The RAG Pipeline

                    1. **User Query** — The user asks a question.
                    2. **Retrieval** — The system searches a knowledge base (using vector search) for relevant documents.
                    3. **Augmentation** — Retrieved documents are added to the prompt as context.
                    4. **Generation** — The LLM generates a response grounded in the retrieved context.

                    ## Benefits

                    - **Accuracy**: Responses are grounded in actual data, reducing hallucinations.
                    - **Freshness**: The knowledge base can be updated without retraining the model.
                    - **Transparency**: Sources can be cited alongside the generated answer.

                    ## Implementation Tips

                    - Use chunk sizes of 500–1000 tokens for best retrieval quality.
                    - Overlap chunks by 10–20% to preserve context boundaries.
                    - Always include metadata (source, date) with each chunk.
                    """,
            },
            new Article
            {
                ItemId = UniqueId.GenerateId(),
                Title = "Search Indexing with Elasticsearch",
                CreatedUtc = DateTime.UtcNow,
                Description = """
                    # Search Indexing with Elasticsearch

                    Elasticsearch is a distributed search and analytics engine built on Apache Lucene. It supports full-text search, structured queries, and dense vector search for AI workloads.

                    ## Core Concepts

                    - **Index**: A collection of documents with a defined schema (mapping).
                    - **Document**: A JSON object stored in an index.
                    - **Mapping**: Defines field types (keyword, text, dense_vector, date, etc.).

                    ## Best Practices

                    1. Use `keyword` for IDs and exact-match filters.
                    2. Use `text` for full-text searchable fields.
                    3. Keep index mappings minimal — only index fields you need to query.
                    4. Use bulk operations for efficient batch indexing.
                    """,
            },
            new Article
            {
                ItemId = UniqueId.GenerateId(),
                Title = "Azure AI Search for Knowledge Bases",
                CreatedUtc = DateTime.UtcNow,
                Description = """
                    # Azure AI Search for Knowledge Bases

                    Azure AI Search (formerly Azure Cognitive Search) is a fully managed cloud search service that supports full-text search, vector search, and hybrid queries.

                    ## Key Features

                    - **Vector Search**: Built-in support for HNSW-based approximate nearest neighbor search.
                    - **Semantic Ranking**: AI-powered re-ranking of search results for better relevance.
                    - **Integrated Vectorization**: Automatic embedding generation during indexing.
                    - **Hybrid Search**: Combine keyword and vector search in a single query.

                    ## Creating an Index

                    Define fields with appropriate types:

                    - `Edm.String` with `searchable: true` for text fields.
                    - `Edm.String` with `filterable: true` for keyword fields.
                    - `Collection(Edm.Single)` for vector fields with HNSW configuration.

                    ## Integration Patterns

                    Azure AI Search integrates naturally with Azure OpenAI for RAG scenarios. The "On Your Data" feature allows direct connection between Azure OpenAI chat completions and an Azure AI Search index.
                    """,
            },
            new Article
            {
                ItemId = UniqueId.GenerateId(),
                Title = "Building Custom Data Sources for AI",
                CreatedUtc = DateTime.UtcNow,
                Description = """
                    # Building Custom Data Sources for AI

                    A data source connects your application's structured data to AI search indexes. This allows AI assistants to answer questions using your specific domain knowledge.

                    ## Architecture Overview

                    ```
                    Application Data → Indexing Service → Search Index → Data Source → AI Profile
                    ```

                    1. **Application Data**: Your domain models (articles, products, tickets, etc.).
                    2. **Indexing Service**: Transforms models into search documents with defined field mappings.
                    3. **Search Index**: Stores documents in Elasticsearch or Azure AI Search.
                    4. **Data Source**: Maps the search index to the AI system with field name configuration.
                    5. **AI Profile**: Uses data sources as knowledge bases during chat.

                    ## Keeping Data in Sync

                    Use catalog handlers (`ICatalogEntryHandler<T>`) to automatically trigger re-indexing whenever a record is created, updated, or deleted. This ensures the search index always reflects the current state of your data.

                    ## Field Mapping

                    Define your index fields carefully:
                    - **Key field**: Unique identifier (usually the record ID).
                    - **Searchable fields**: Title, description, content — fields users will search.
                    - **Filterable fields**: Author, category, date — fields used for filtering.
                    """,
            },
        };
        foreach (var article in articles)
        {
            await catalog.CreateAsync(article);
        }

        await services.GetRequiredService<YesSql.ISession>().SaveChangesAsync();
    }
}
