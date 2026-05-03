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

public sealed class YesSqlAIChatSessionIndexPersistenceTests
{
    [Fact]
    public async Task SaveAndCommitAsync_WritesRowToAIChatSessionIndexTable()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"{nameof(YesSqlAIChatSessionIndexPersistenceTests)}-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        await using var rootConnection = new SqliteConnection(connectionString);
        await rootConnection.OpenAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        services.AddSingleton(TimeProvider.System);
        services.AddCoreYesSqlDataStore(configuration => configuration.UseSqLite(connectionString));
        services.AddCoreAIChatSessionBaseStoresYesSql();

        await using var serviceProvider = services.BuildServiceProvider();
        await InitializeSchemaAsync(serviceProvider);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var sessionManager = scope.ServiceProvider.GetRequiredService<IAIChatSessionManager>();
            var committer = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();

            var session = await sessionManager.NewAsync(new AIProfile
            {
                ItemId = "profile-1",
                Type = AIProfileType.Chat,
            }, new NewAIChatSessionContext(), TestContext.Current.CancellationToken);

            session.Title = "Support session";

            await sessionManager.SaveAsync(session, TestContext.Current.CancellationToken);
            await committer.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AI_AIChatSessionIndex;";

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveAndCommitAsync_WhenExistingSessionIsSavedInNewScope_DoesNotCreateDuplicateDocuments()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"{nameof(YesSqlAIChatSessionIndexPersistenceTests)}-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        await using var rootConnection = new SqliteConnection(connectionString);
        await rootConnection.OpenAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        services.AddSingleton(TimeProvider.System);
        services.AddCoreYesSqlDataStore(configuration => configuration.UseSqLite(connectionString));
        services.AddCoreAIChatSessionBaseStoresYesSql();

        await using var serviceProvider = services.BuildServiceProvider();
        await InitializeSchemaAsync(serviceProvider);

        string sessionId;
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var sessionManager = scope.ServiceProvider.GetRequiredService<IAIChatSessionManager>();
            var committer = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();

            var session = await sessionManager.NewAsync(new AIProfile
            {
                ItemId = "profile-1",
                Type = AIProfileType.Chat,
            }, new NewAIChatSessionContext(), TestContext.Current.CancellationToken);

            session.Title = "Created";
            await sessionManager.SaveAsync(session, TestContext.Current.CancellationToken);
            await committer.CommitAsync(TestContext.Current.CancellationToken);
            sessionId = session.SessionId;
        }

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var sessionManager = scope.ServiceProvider.GetRequiredService<IAIChatSessionManager>();
            var committer = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();
            var session = await sessionManager.FindByIdAsync(sessionId, TestContext.Current.CancellationToken);

            Assert.NotNull(session);

            session.Title = "Updated";
            await sessionManager.SaveAsync(session, TestContext.Current.CancellationToken);
            await committer.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM AI_Document
            WHERE Type = 'CrestApps.Core.AI.Models.AIChatSession, CrestApps.Core.AI.Abstractions'
              AND Content LIKE '%' || $sessionId || '%';
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);

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

        await schemaBuilder.CreateAIChatSessionIndexSchemaAsync(options);
        await schemaBuilder.CreateAIChatSessionPromptIndexSchemaAsync(options);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }
}
