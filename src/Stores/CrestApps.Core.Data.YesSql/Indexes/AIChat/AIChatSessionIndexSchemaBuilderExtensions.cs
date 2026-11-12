using CrestApps.Core.AI.Models;
using YesSql;
using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

public static class AIChatSessionIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates ai chat session index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIChatSessionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIChatSessionIndex>(table => table
            .Column<string>(nameof(AIChatSessionIndex.SessionId), column => column.WithLength(26))
            .Column<string>(nameof(AIChatSessionIndex.ProfileId), column => column.WithLength(26))
            .Column<string>(nameof(AIChatSessionIndex.UserId), column => column.WithLength(255))
            .Column<string>(nameof(AIChatSessionIndex.ClientId), column => column.WithLength(255))
            .Column<ChatSessionStatus>(nameof(AIChatSessionIndex.Status))
            .Column<DateTime>(nameof(AIChatSessionIndex.LastActivityUtc)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIChatSessionIndex>(
            table => table.CreateIndex("IDX_AIChatSession_DocumentId", "DocumentId", nameof(AIChatSessionIndex.SessionId)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIChatSessionIndex>(
            table => table.CreateIndex("IDX_AIChatSession_ProfileId", "DocumentId", nameof(AIChatSessionIndex.ProfileId)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIChatSessionIndex>(
            table => table.CreateIndex("IDX_AIChatSession_ClientId", "DocumentId", nameof(AIChatSessionIndex.ClientId)),
            collection: options?.AICollectionName);
    }

    /// <summary>
    /// Adds the <see cref="AIChatSessionIndex.ClientId"/> column, and an index on it, to an existing chat session index
    /// table. Call it from a host's migration when upgrading a database that was created before the column existed; a new
    /// database gets the column from <see cref="CreateAIChatSessionIndexSchemaAsync"/>.
    /// </summary>
    /// <remarks>
    /// It can be run any number of times: it adds the column only when it is missing, and creates the index only when it
    /// is missing, so a run that stopped between the two is finished by the next one, and a host can call it at every
    /// start. Rows written before the upgrade have no client id until their session is saved again, or until
    /// <see cref="BackfillAIChatSessionIndexClientIdAsync"/> is run.
    /// <para>
    /// SQLite, SQL Server, PostgreSQL and MySQL are checked before anything is changed. On another provider the column and
    /// the index are created without a check, as they were before, and a second run fails.
    /// </para>
    /// </remarks>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task AddAIChatSessionIndexClientIdColumnAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);

        const string indexName = "IDX_AIChatSession_ClientId";
        var table = schemaBuilder.TablePrefix + schemaBuilder.TableNameConvention.GetIndexTable(typeof(AIChatSessionIndex), options?.AICollectionName);

        if (await ExistsAsync(schemaBuilder, ObjectKind.Column, table, nameof(AIChatSessionIndex.ClientId)) != true)
        {
            await schemaBuilder.AlterIndexTableAsync<AIChatSessionIndex>(t =>
            {
                t.AddColumn<string>(nameof(AIChatSessionIndex.ClientId), column => column.WithLength(255));
            }, collection: options?.AICollectionName);
        }

        if (await ExistsAsync(schemaBuilder, ObjectKind.Index, table, indexName) != true)
        {
            await schemaBuilder.AlterIndexTableAsync<AIChatSessionIndex>(
                t => t.CreateIndex(indexName, "DocumentId", nameof(AIChatSessionIndex.ClientId)),
                collection: options?.AICollectionName);
        }
    }

    /// <summary>
    /// Indexes the client id of the anonymous chat sessions that were saved before the index had one. YesSql fills an index
    /// when it maps a document, so a session is saved again for its row to get the value. Call it once after
    /// <see cref="AddAIChatSessionIndexClientIdColumnAsync"/>, and commit the session; without it, once a host lists
    /// anonymous history by <see cref="AIChatSessionIndex.ClientId"/>, a visitor's older sessions would be missing.
    /// </summary>
    /// <param name="session">The YesSql session.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of sessions that were saved again.</returns>
    public static async Task<int> BackfillAIChatSessionIndexClientIdAsync(this ISession session, YesSqlStoreOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var collection = options?.AICollectionName;
        var sessions = await session
            .Query<AIChatSession, AIChatSessionIndex>(index => index.UserId == null && index.ClientId == null, collection: collection)
            .ListAsync(cancellationToken);
        var count = 0;

        foreach (var chatSession in sessions)
        {
            if (string.IsNullOrEmpty(chatSession.ClientId))
            {
                continue;
            }

            await session.SaveAsync(chatSession, false, collection, cancellationToken);
            count++;
        }

        return count;
    }

    private enum ObjectKind
    {
        Column,
        Index,
    }

    // Whether the column or index exists, or null when the provider is not one that is checked.
    private static async Task<bool?> ExistsAsync(ISchemaBuilder schemaBuilder, ObjectKind kind, string table, string name)
    {
        var sql = (schemaBuilder.Dialect.Name, kind) switch
        {
            ("Sqlite", ObjectKind.Column) => "SELECT COUNT(*) FROM pragma_table_info(@table) WHERE name = @name",
            ("Sqlite", ObjectKind.Index) => "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND tbl_name = @table AND name IN (@name, @prefixedName)",
            ("SqlServer", ObjectKind.Column) => "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND COLUMN_NAME = @name",
            ("SqlServer", ObjectKind.Index) => "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(@table) AND name IN (@name, @prefixedName)",
            ("PostgreSql", ObjectKind.Column) => "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @table AND column_name = @name",
            ("PostgreSql", ObjectKind.Index) => "SELECT COUNT(*) FROM pg_indexes WHERE tablename = @table AND indexname IN (@name, @prefixedName)",
            ("MySql", ObjectKind.Column) => "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @table AND column_name = @name",
            ("MySql", ObjectKind.Index) => "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = @table AND index_name IN (@name, @prefixedName)",
            _ => null,
        };

        if (sql is null || schemaBuilder.Connection is null)
        {
            return null;
        }

        await using var command = schemaBuilder.Connection.CreateCommand();
        command.Transaction = schemaBuilder.Transaction;
        command.CommandText = sql;

        AddParameter(command, "@table", table);
        AddParameter(command, "@name", name);

        if (kind == ObjectKind.Index)
        {
            AddParameter(command, "@prefixedName", schemaBuilder.TablePrefix + name);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();

        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
