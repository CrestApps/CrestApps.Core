using CrestApps.Core.AI.Models;
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
    /// table. Call it once from a host's migration when upgrading a database that was created before the column existed;
    /// a new database gets the column from <see cref="CreateAIChatSessionIndexSchemaAsync"/>.
    /// </summary>
    /// <remarks>
    /// Rows written before the upgrade have no client id until their session is saved again, because YesSql fills an
    /// index when it maps a document. Sessions that belong to a signed-in user are unaffected: only anonymous visitors'
    /// sessions carry a client id.
    /// </remarks>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task AddAIChatSessionIndexClientIdColumnAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);

        await schemaBuilder.AlterIndexTableAsync<AIChatSessionIndex>(table =>
        {
            table.AddColumn<string>(nameof(AIChatSessionIndex.ClientId), column => column.WithLength(255));
        }, collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIChatSessionIndex>(
            table => table.CreateIndex("IDX_AIChatSession_ClientId", "DocumentId", nameof(AIChatSessionIndex.ClientId)),
            collection: options?.AICollectionName);
    }
}
