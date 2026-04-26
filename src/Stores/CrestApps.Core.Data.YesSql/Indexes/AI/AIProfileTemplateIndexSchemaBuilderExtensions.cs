using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public static class AIProfileTemplateIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates ai profile template index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIProfileTemplateIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIProfileTemplateIndex>(table => table
            .Column<string>(nameof(AIProfileTemplateIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIProfileTemplateIndex.Name), column => column.WithLength(255))
            .Column<string>(nameof(AIProfileTemplateIndex.Source), column => column.WithLength(255)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIProfileTemplateIndex>(table =>
        {
            table.CreateIndex("IDX_AIProfileTemplate_DocumentId", "DocumentId", nameof(AIProfileTemplateIndex.Name));
            table.CreateIndex("IDX_AIProfileTemplate_Source", "DocumentId", nameof(AIProfileTemplateIndex.Source));
        }, collection: options?.AICollectionName);
    }
}
