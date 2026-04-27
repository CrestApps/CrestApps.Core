using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public static class AIProfileIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates ai profile index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIProfileIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);

        await schemaBuilder.CreateMapIndexTableAsync<AIProfileIndex>(table => table
            .Column<string>(nameof(AIProfileIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIProfileIndex.Name), column => column.WithLength(255))
            .Column<string>(nameof(AIProfileIndex.Source), column => column.WithLength(255))
            .Column<string>(nameof(AIProfileIndex.Type), column => column.WithLength(50)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIProfileIndex>(table =>
        {
            table.CreateIndex("IDX_AIProfile_DocumentId", "DocumentId", nameof(AIProfileIndex.Name));
            table.CreateIndex("IDX_AIProfile_Source", "DocumentId", nameof(AIProfileIndex.Source));
            table.CreateIndex("IDX_AIProfile_Type", "DocumentId", nameof(AIProfileIndex.Type));
        }, collection: options?.AICollectionName);
    }

    /// <summary>
    /// Adds the AI profile type column to an existing AI profile index table.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static Task AddAIProfileIndexTypeColumnAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);

        return schemaBuilder.AlterIndexTableAsync<AIProfileIndex>(table =>
        {
            table.AddColumn<string>(nameof(AIProfileIndex.Type), column => column.WithLength(50));
            table.CreateIndex("IDX_AIProfile_Type", "DocumentId", nameof(AIProfileIndex.Type));
        }, collection: options?.AICollectionName);
    }
}
