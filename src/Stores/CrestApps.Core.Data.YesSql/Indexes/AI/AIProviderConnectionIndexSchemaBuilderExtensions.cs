using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public static class AIProviderConnectionIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates ai provider connection index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIProviderConnectionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIProviderConnectionIndex>(table => table
            .Column<string>(nameof(AIProviderConnectionIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIProviderConnectionIndex.Name), column => column.WithLength(255))
            .Column<string>(nameof(AIProviderConnectionIndex.Source), column => column.WithLength(255)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIProviderConnectionIndex>(table =>
        {
            table.CreateIndex("IDX_AIProviderConnection_DocumentId", "DocumentId", nameof(AIProviderConnectionIndex.Name));
            table.CreateIndex("IDX_AIProviderConnection_Source", "DocumentId", nameof(AIProviderConnectionIndex.Source));
        }, collection: options?.AICollectionName);
    }
}
