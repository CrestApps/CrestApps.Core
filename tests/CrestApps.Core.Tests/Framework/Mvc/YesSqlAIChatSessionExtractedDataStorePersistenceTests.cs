using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using CrestApps.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class YesSqlAIChatSessionExtractedDataStorePersistenceTests
{
    [Fact]
    public async Task SaveAndCommitAsync_WritesAndReadsExtractedDataSnapshot()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"{nameof(YesSqlAIChatSessionExtractedDataStorePersistenceTests)}-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        await using var rootConnection = new SqliteConnection(connectionString);
        await rootConnection.OpenAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(TimeProvider.System);
        services.AddCoreYesSqlDataStore(configuration => configuration.UseSqLite(connectionString));
        services.AddCoreAIChatSessionExtractedDataStoresYesSql();

        await using var serviceProvider = services.BuildServiceProvider();
        await InitializeSchemaAsync(serviceProvider);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var extractedDataStore = scope.ServiceProvider.GetRequiredService<IAIChatSessionExtractedDataStore>();
            var committer = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();

            await extractedDataStore.SaveAsync(
                new AIChatSessionExtractedDataRecord
                {
                    ItemId = "session-1",
                    SessionId = "session-1",
                    ProfileId = "profile-1",
                    SessionStartedUtc = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
                    SessionEndedUtc = new DateTime(2026, 5, 1, 12, 5, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 5, 1, 12, 5, 0, DateTimeKind.Utc),
                    Values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["customer_name"] = ["Mike Alhayek"],
                        ["customer_phone"] = ["7024993350"],
                    },
                },
                TestContext.Current.CancellationToken);

            await committer.CommitAsync(TestContext.Current.CancellationToken);

            var records = await extractedDataStore.GetAsync("profile-1", null, null, TestContext.Current.CancellationToken);

            Assert.Single(records);
            Assert.Equal("session-1", records[0].SessionId);
            Assert.Equal("Mike Alhayek", Assert.Single(records[0].Values["customer_name"]));
            Assert.Equal("7024993350", Assert.Single(records[0].Values["customer_phone"]));
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AI_AIChatSessionExtractedDataIndex;";

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

        await schemaBuilder.CreateAIChatSessionExtractedDataIndexSchemaAsync(options);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }
}
