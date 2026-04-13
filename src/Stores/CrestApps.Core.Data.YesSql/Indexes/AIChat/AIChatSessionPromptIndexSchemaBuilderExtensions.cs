using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

public static class AIChatSessionPromptIndexSchemaBuilderExtensions
{
    public static Task CreateAIChatSessionPromptIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options = null)
    {
        options ??= new YesSqlStoreOptions();

        return schemaBuilder.CreateMapIndexTableAsync<AIChatSessionPromptIndex>(table => table
            .Column<string>(nameof(AIChatSessionPromptIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIChatSessionPromptIndex.SessionId), column => column.WithLength(26))
            .Column<string>(nameof(AIChatSessionPromptIndex.Role), column => column.WithLength(50)),
            collection: options.AICollectionName);
    }
}
