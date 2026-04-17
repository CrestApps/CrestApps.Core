using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public static class AIProfileIndexSchemaBuilderExtensions
{
    public static async Task CreateAIProfileIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);

        await schemaBuilder.CreateMapIndexTableAsync<AIProfileIndex>(table => table
            .Column<string>(nameof(AIProfileIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIProfileIndex.Name), column => column.WithLength(255))
            .Column<string>(nameof(AIProfileIndex.Source), column => column.WithLength(255)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIProfileIndex>(table =>
        {
            table.CreateIndex("IDX_AIProfile_DocumentId", "DocumentId", nameof(AIProfileIndex.Name));
            table.CreateIndex("IDX_AIProfile_Source", "DocumentId", nameof(AIProfileIndex.Source));
        }, collection: options?.AICollectionName);
    }
}
