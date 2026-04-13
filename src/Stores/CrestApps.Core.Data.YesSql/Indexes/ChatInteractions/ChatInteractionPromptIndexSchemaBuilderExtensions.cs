using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;

public static class ChatInteractionPromptIndexSchemaBuilderExtensions
{
    public static Task CreateChatInteractionPromptIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options = null)
    {
        options ??= new YesSqlStoreOptions();

        return schemaBuilder.CreateMapIndexTableAsync<ChatInteractionPromptIndex>(table => table
            .Column<string>(nameof(ChatInteractionPromptIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(ChatInteractionPromptIndex.ChatInteractionId), column => column.WithLength(26))
            .Column<string>(nameof(ChatInteractionPromptIndex.Role), column => column.WithLength(50))
            .Column<DateTime>(nameof(ChatInteractionPromptIndex.CreatedUtc)),
            collection: options.AICollectionName);
    }
}
