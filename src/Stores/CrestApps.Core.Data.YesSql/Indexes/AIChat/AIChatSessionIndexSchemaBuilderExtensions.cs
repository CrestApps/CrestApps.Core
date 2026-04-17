using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

public static class AIChatSessionIndexSchemaBuilderExtensions
{
    public static async Task CreateAIChatSessionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIChatSessionIndex>(table => table
            .Column<string>(nameof(AIChatSessionIndex.SessionId), column => column.WithLength(26))
            .Column<string>(nameof(AIChatSessionIndex.ProfileId), column => column.WithLength(26))
            .Column<string>(nameof(AIChatSessionIndex.UserId), column => column.WithLength(255))
            .Column<int>(nameof(AIChatSessionIndex.Status))
            .Column<DateTime>(nameof(AIChatSessionIndex.LastActivityUtc)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIChatSessionIndex>(table =>
        {
            table.CreateIndex("IDX_AIChatSession_DocumentId", "DocumentId", nameof(AIChatSessionIndex.SessionId));
            table.CreateIndex("IDX_AIChatSession_ProfileId", "DocumentId", nameof(AIChatSessionIndex.ProfileId));
        }, collection: options?.AICollectionName);
    }
}
