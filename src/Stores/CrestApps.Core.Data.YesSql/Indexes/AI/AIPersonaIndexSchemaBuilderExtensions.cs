using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// Creates the YesSql index table for AI personas.
/// </summary>
public static class AIPersonaIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates the AI persona index table and its lookup index.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIPersonaIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);

        await schemaBuilder.CreateMapIndexTableAsync<AIPersonaIndex>(table => table
            .Column<string>(nameof(AIPersonaIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIPersonaIndex.Name), column => column.WithLength(255)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIPersonaIndex>(table =>
        {
            table.CreateIndex("IDX_AIPersona_DocumentId", "DocumentId", nameof(AIPersonaIndex.Name));
        }, collection: options?.AICollectionName);
    }
}
