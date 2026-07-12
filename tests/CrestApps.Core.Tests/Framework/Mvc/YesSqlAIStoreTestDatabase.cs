using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace CrestApps.Core.Tests.Framework.Mvc;

/// <summary>
/// Provides an isolated in-memory YesSql SQLite database for AI store tests.
/// </summary>
internal sealed class YesSqlAIStoreTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _rootConnection;

    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAIStoreTestDatabase"/> class.
    /// </summary>
    /// <param name="rootConnection">The root connection that keeps the in-memory database alive.</param>
    /// <param name="store">The initialized YesSql store.</param>
    private YesSqlAIStoreTestDatabase(
        SqliteConnection rootConnection,
        IStore store)
    {
        _rootConnection = rootConnection;
        Store = store;
    }

    /// <summary>
    /// Gets the initialized YesSql store.
    /// </summary>
    public IStore Store { get; }

    /// <summary>
    /// Creates an isolated database with the requested AI collections and schemas.
    /// </summary>
    /// <param name="collectionNames">The collection names to initialize.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The initialized test database.</returns>
    public static async Task<YesSqlAIStoreTestDatabase> CreateAsync(
        IReadOnlyCollection<string> collectionNames,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"{nameof(YesSqlAIStoreTestDatabase)}-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        var rootConnection = new SqliteConnection(connectionString);
        await rootConnection.OpenAsync(cancellationToken);
        IStore store = null;

        try
        {
            store = await StoreFactory.CreateAndInitializeAsync(
                new Configuration().UseSqLite(connectionString));

            foreach (var collectionName in collectionNames)
            {
                await InitializeCollectionAsync(store, collectionName, cancellationToken);
            }

            return new YesSqlAIStoreTestDatabase(rootConnection, store);
        }
        catch
        {
            store?.Dispose();
            await rootConnection.DisposeAsync();

            throw;
        }
    }

    /// <summary>
    /// Saves and commits the provided documents in the requested collection.
    /// </summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="documents">The documents to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync<T>(
        string collectionName,
        IEnumerable<T> documents,
        CancellationToken cancellationToken)
    {
        await using var session = Store.CreateSession();

        foreach (var document in documents)
        {
            await session.SaveAsync(document, false, collectionName, cancellationToken);
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes the YesSql store and root SQLite connection.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Store.Dispose();
        await _rootConnection.DisposeAsync();
    }

    /// <summary>
    /// Initializes the YesSql collection, index providers, and AI map-index schemas.
    /// </summary>
    /// <param name="store">The YesSql store.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task InitializeCollectionAsync(
        IStore store,
        string collectionName,
        CancellationToken cancellationToken)
    {
        var options = new YesSqlStoreOptions
        {
            AICollectionName = collectionName,
        };

        await store.InitializeCollectionAsync(collectionName, cancellationToken);
        store.RegisterIndexes(
            [
                new AIChatSessionPromptIndexProvider(Options.Create(options)),
                new AICompletionUsageIndexProvider(Options.Create(options)),
                new AIChatSessionMetricsIndexProvider(Options.Create(options)),
            ],
            collectionName);

        await using var connection = store.Configuration.ConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var schemaBuilder = new SchemaBuilder(store.Configuration, transaction);

        await CreateSchemaAsync(() => schemaBuilder.CreateAIChatSessionPromptIndexSchemaAsync(options));
        await CreateSchemaAsync(() => schemaBuilder.CreateAICompletionUsageIndexSchemaAsync(options));
        await schemaBuilder.CreateAIChatSessionMetricsSchemaAsync(
            options,
            new AIChatSessionMetricsIndexSchemaOptions());
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a collection schema while tolerating SQLite's database-global named index collisions.
    /// </summary>
    /// <param name="createSchema">The schema creation operation.</param>
    private static async Task CreateSchemaAsync(Func<Task> createSchema)
    {
        try
        {
            await createSchema();
        }
        catch (SqliteException exception) when (
            exception.SqliteErrorCode == 1 &&
            exception.Message.Contains("index ", StringComparison.Ordinal) &&
            exception.Message.Contains(" already exists", StringComparison.Ordinal))
        {
            // SQLite index names are database-wide; the collection table was created before this error.
        }
    }
}
