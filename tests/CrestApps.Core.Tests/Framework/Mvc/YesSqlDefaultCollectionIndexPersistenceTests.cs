using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.Indexing;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class YesSqlDefaultCollectionIndexPersistenceTests
{
    [Fact]
    public async Task SaveAndCommitAsync_WritesRowToConfiguredDefaultCollectionIndexTable()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"{nameof(YesSqlDefaultCollectionIndexPersistenceTests)}-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        await using var rootConnection = new SqliteConnection(connectionString);
        await rootConnection.OpenAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(TimeProvider.System);
        services.Configure<YesSqlStoreOptions>(options => options.DefaultCollectionName = "Default");
        services.AddCoreYesSqlDataStore(configuration => configuration.UseSqLite(connectionString));
        services.AddCoreIndexingStoresYesSql();

        await using var serviceProvider = services.BuildServiceProvider();
        await InitializeSchemaAsync(serviceProvider);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISearchIndexProfileStore>();
            var committer = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();

            await store.CreateAsync(new SearchIndexProfile
            {
                ItemId = "profile-1",
                Name = "articles",
                ProviderName = "Elasticsearch",
                IndexName = "articles",
                IndexFullName = "sample-articles",
                Type = "articles",
                CreatedUtc = new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc),
            }, TestContext.Current.CancellationToken);

            await committer.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Default_SearchIndexProfileIndex;";

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, count);
    }

    private static async Task InitializeSchemaAsync(IServiceProvider services)
    {
        var store = services.GetRequiredService<IStore>();
        var options = services.GetRequiredService<IOptions<YesSqlStoreOptions>>().Value;

        await using var connection = store.Configuration.ConnectionFactory.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var schemaBuilder = new SchemaBuilder(store.Configuration, transaction);

        await schemaBuilder.CreateSearchIndexProfileIndexSchemaAsync(options);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }
}
