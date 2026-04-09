using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;

public static class ChatInteractionIndexSchemaBuilderExtensions
{
    public static Task CreateChatInteractionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, string collection = null)
    {
        return schemaBuilder.CreateMapIndexTableAsync<ChatInteractionIndex>(table => table
            .Column<string>(nameof(ChatInteractionIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(ChatInteractionIndex.UserId), column => column.WithLength(255))
            .Column<string>(nameof(ChatInteractionIndex.Title), column => column.WithLength(255))
            .Column<DateTime>(nameof(ChatInteractionIndex.CreatedUtc)),
            collection: collection);
    }
}
