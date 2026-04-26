using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public static class AIDeploymentIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates ai deployment index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIDeploymentIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIDeploymentIndex>(table => table
            .Column<string>(nameof(AIDeploymentIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIDeploymentIndex.Name), column => column.WithLength(255))
            .Column<string>(nameof(AIDeploymentIndex.Source), column => column.WithLength(255)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIDeploymentIndex>(table =>
        {
            table.CreateIndex("IDX_AIDeployment_DocumentId", "DocumentId", nameof(AIDeploymentIndex.Name));
            table.CreateIndex("IDX_AIDeployment_Source", "DocumentId", nameof(AIDeploymentIndex.Source));
        }, collection: options?.AICollectionName);
    }
}
