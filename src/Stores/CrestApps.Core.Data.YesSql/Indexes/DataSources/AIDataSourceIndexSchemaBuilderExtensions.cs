using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.DataSources;

public static class AIDataSourceIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates ai data source index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIDataSourceIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIDataSourceIndex>(table => table
            .Column<string>(nameof(AIDataSourceIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIDataSourceIndex.DisplayText), column => column.WithLength(255))
            .Column<string>(nameof(AIDataSourceIndex.SourceIndexProfileName), column => column.WithLength(255)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIDataSourceIndex>(table =>
        {
            table.CreateIndex("IDX_AIDataSource_DocumentId", "DocumentId");
        }, collection: options?.AICollectionName);
    }
}
