using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class YesSqlAIChatSessionIndexClientIdTests
{
    [Fact]
    public async Task NewSchema_IndexesTheClientIdOfAnAnonymousSession()
    {
        await using var harness = await Harness.CreateAsync(upgradeFromOldSchema: false);

        await harness.SaveSessionAsync("session-1", clientId: "visitor-1");

        Assert.Equal("visitor-1", await harness.ReadIndexedClientIdAsync("session-1"));
    }

    [Fact]
    public async Task UpgradedSchema_IndexesTheClientIdOfAnAnonymousSession()
    {
        await using var harness = await Harness.CreateAsync(upgradeFromOldSchema: true);

        await harness.SaveSessionAsync("session-1", clientId: "visitor-1");

        Assert.Equal("visitor-1", await harness.ReadIndexedClientIdAsync("session-1"));
    }

    [Fact]
    public async Task PageAsync_CanListTheSessionsOfOneVisitor()
    {
        await using var harness = await Harness.CreateAsync(upgradeFromOldSchema: false);

        await harness.SaveSessionAsync("session-1", clientId: "visitor-1");
        await harness.SaveSessionAsync("session-2", clientId: "visitor-2");
        await harness.SaveSessionAsync("session-3", clientId: "visitor-1");

        var result = await harness.PageAsync(new AIChatSessionQueryContext { ClientId = "visitor-1" });

        Assert.Equal(2, result.Count);
        Assert.All(result.Sessions, session => Assert.Equal("visitor-1", session.ClientId));
    }

    [Fact]
    public async Task UpgradeMethod_CanBeRunOnlyOnceAgainstTheSameTable()
    {
        await using var harness = await Harness.CreateAsync(upgradeFromOldSchema: true);

        // The column already exists, so a second run is rejected rather than silently doing nothing; hosts call the upgrade
        // from a migration that runs once.
        await Assert.ThrowsAnyAsync<Exception>(() => harness.UpgradeAsync());
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _root;
        private readonly ServiceProvider _services;
        private readonly string _connectionString;

        private Harness(SqliteConnection root, ServiceProvider services, string connectionString)
        {
            _root = root;
            _services = services;
            _connectionString = connectionString;
        }

        public static async Task<Harness> CreateAsync(bool upgradeFromOldSchema)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"{nameof(YesSqlAIChatSessionIndexClientIdTests)}-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
            }.ToString();

            var root = new SqliteConnection(connectionString);
            await root.OpenAsync(TestContext.Current.CancellationToken);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            services.AddSingleton(TimeProvider.System);
            services.AddCoreYesSqlDataStore(configuration => configuration.UseSqLite(connectionString));
            services.AddCoreAIChatSessionBaseStoresYesSql();

            var provider = services.BuildServiceProvider();
            var harness = new Harness(root, provider, connectionString);

            await harness.InitializeSchemaAsync(upgradeFromOldSchema);

            return harness;
        }

        public async Task SaveSessionAsync(string sessionId, string clientId)
        {
            await using var scope = _services.CreateAsyncScope();
            var sessionManager = scope.ServiceProvider.GetRequiredService<IAIChatSessionManager>();
            var committer = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();
            var session = await sessionManager.NewAsync(
                new AIProfile { ItemId = "profile-1", Type = AIProfileType.Chat },
                new NewAIChatSessionContext(),
                TestContext.Current.CancellationToken);

            session.SessionId = sessionId;
            session.UserId = null;
            session.ClientId = clientId;

            await sessionManager.SaveAsync(session, TestContext.Current.CancellationToken);
            await committer.CommitAsync(TestContext.Current.CancellationToken);
        }

        public async Task<AIChatSessionResult> PageAsync(AIChatSessionQueryContext context)
        {
            await using var scope = _services.CreateAsyncScope();

            return await scope.ServiceProvider.GetRequiredService<IAIChatSessionManager>()
                .PageAsync(1, 20, context, TestContext.Current.CancellationToken);
        }

        public async Task<string> ReadIndexedClientIdAsync(string sessionId)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT ClientId FROM AI_AIChatSessionIndex WHERE SessionId = $sessionId;";
            command.Parameters.AddWithValue("$sessionId", sessionId);

            var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

            return value is null or DBNull ? null : (string)value;
        }

        public async Task UpgradeAsync()
        {
            var store = _services.GetRequiredService<IStore>();
            var options = _services.GetRequiredService<IOptions<YesSqlStoreOptions>>().Value;

            await using var connection = store.Configuration.ConnectionFactory.CreateConnection();
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

            await new SchemaBuilder(store.Configuration, transaction).AddAIChatSessionIndexClientIdColumnAsync(options);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await _root.DisposeAsync();
        }

        private async Task InitializeSchemaAsync(bool upgradeFromOldSchema)
        {
            var store = _services.GetRequiredService<IStore>();
            var options = _services.GetRequiredService<IOptions<YesSqlStoreOptions>>().Value;

            await using (var connection = store.Configuration.ConnectionFactory.CreateConnection())
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
                var schemaBuilder = new SchemaBuilder(store.Configuration, transaction);

                if (upgradeFromOldSchema)
                {
                    // The table as an earlier version created it: no ClientId column.
                    await schemaBuilder.CreateMapIndexTableAsync<AIChatSessionIndex>(table => table
                        .Column<string>(nameof(AIChatSessionIndex.SessionId), column => column.WithLength(26))
                        .Column<string>(nameof(AIChatSessionIndex.ProfileId), column => column.WithLength(26))
                        .Column<string>(nameof(AIChatSessionIndex.UserId), column => column.WithLength(255))
                        .Column<ChatSessionStatus>(nameof(AIChatSessionIndex.Status))
                        .Column<DateTime>(nameof(AIChatSessionIndex.LastActivityUtc)),
                        collection: options.AICollectionName);
                }
                else
                {
                    await schemaBuilder.CreateAIChatSessionIndexSchemaAsync(options);
                }

                await schemaBuilder.CreateAIChatSessionPromptIndexSchemaAsync(options);
                await transaction.CommitAsync(TestContext.Current.CancellationToken);
            }

            if (upgradeFromOldSchema)
            {
                await UpgradeAsync();
            }
        }
    }
}
