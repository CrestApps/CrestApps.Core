using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AIMemory;

public static class AIMemoryEntryIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates ai memory entry index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIMemoryEntryIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIMemoryEntryIndex>(table => table
            .Column<string>(nameof(AIMemoryEntryIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIMemoryEntryIndex.UserId), column => column.WithLength(255))
            .Column<string>(nameof(AIMemoryEntryIndex.Name), column => column.WithLength(255)),
            collection: options?.AIMemoryCollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIMemoryEntryIndex>(table =>
        {
            table.CreateIndex("IDX_AIMemoryEntry_DocumentId", "DocumentId", nameof(AIMemoryEntryIndex.UserId), nameof(AIMemoryEntryIndex.Name));
        }, collection: options?.AIMemoryCollectionName);
    }
}
