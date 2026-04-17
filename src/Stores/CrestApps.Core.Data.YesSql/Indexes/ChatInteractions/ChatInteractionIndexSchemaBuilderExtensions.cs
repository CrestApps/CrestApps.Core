using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;

public static class ChatInteractionIndexSchemaBuilderExtensions
{
    public static async Task CreateChatInteractionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<ChatInteractionIndex>(table => table
            .Column<string>(nameof(ChatInteractionIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(ChatInteractionIndex.UserId), column => column.WithLength(255))
            .Column<string>(nameof(ChatInteractionIndex.Title), column => column.WithLength(255))
            .Column<DateTime>(nameof(ChatInteractionIndex.CreatedUtc)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<ChatInteractionIndex>(table =>
        {
            table.CreateIndex("IDX_ChatInteraction_DocumentId", "DocumentId", nameof(ChatInteractionIndex.UserId));
        }, collection: options?.AICollectionName);
    }
}
