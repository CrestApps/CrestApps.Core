using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AIMemory;

public static class AIMemoryEntryIndexSchemaBuilderExtensions
{
    public static Task CreateAIMemoryEntryIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options = null)
    {
        options ??= new YesSqlStoreOptions();

        return schemaBuilder.CreateMapIndexTableAsync<AIMemoryEntryIndex>(table => table
            .Column<string>(nameof(AIMemoryEntryIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIMemoryEntryIndex.UserId), column => column.WithLength(255))
            .Column<string>(nameof(AIMemoryEntryIndex.Name), column => column.WithLength(255)),
            collection: options.AIMemoryCollectionName);
    }
}
