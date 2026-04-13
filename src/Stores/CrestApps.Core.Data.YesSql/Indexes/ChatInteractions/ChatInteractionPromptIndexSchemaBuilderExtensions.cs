using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;

public static class ChatInteractionPromptIndexSchemaBuilderExtensions
{
    public static async Task CreateChatInteractionPromptIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        await schemaBuilder.CreateMapIndexTableAsync<ChatInteractionPromptIndex>(table => table
            .Column<string>(nameof(ChatInteractionPromptIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(ChatInteractionPromptIndex.ChatInteractionId), column => column.WithLength(26))
            .Column<string>(nameof(ChatInteractionPromptIndex.Role), column => column.WithLength(50))
            .Column<DateTime>(nameof(ChatInteractionPromptIndex.CreatedUtc)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<ChatInteractionPromptIndex>(table =>
        {
            table.CreateIndex("IDX_ChatInteractionPrompt_DocumentId", "DocumentId", nameof(ChatInteractionPromptIndex.ChatInteractionId));
        }, collection: options?.AICollectionName);
    }
}
