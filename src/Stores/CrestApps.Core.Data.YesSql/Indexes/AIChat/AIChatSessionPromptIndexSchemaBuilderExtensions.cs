using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

public static class AIChatSessionPromptIndexSchemaBuilderExtensions
{
    public static async Task CreateAIChatSessionPromptIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIChatSessionPromptIndex>(table => table
            .Column<string>(nameof(AIChatSessionPromptIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIChatSessionPromptIndex.SessionId), column => column.WithLength(26))
            .Column<string>(nameof(AIChatSessionPromptIndex.Role), column => column.WithLength(50)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIChatSessionPromptIndex>(table =>
        {
            table.CreateIndex("IDX_AIChatSessionPrompt_DocumentId", "DocumentId", nameof(AIChatSessionPromptIndex.SessionId));
        }, collection: options?.AICollectionName);
    }
}
