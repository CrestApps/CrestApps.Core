using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Tooling;

/// <summary>
/// Schema builder extensions that create the <see cref="AIToolInstanceIndex"/> table.
/// </summary>
public static class AIToolInstanceIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates the AI tool instance index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIToolInstanceIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIToolInstanceIndex>(table => table
            .Column<string>(nameof(AIToolInstanceIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIToolInstanceIndex.DisplayText), column => column.WithLength(255))
            .Column<string>(nameof(AIToolInstanceIndex.Source), column => column.WithLength(50)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIToolInstanceIndex>(
            table => table.CreateIndex("IDX_AIToolInstance_DocumentId", "DocumentId"),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIToolInstanceIndex>(
            table => table.CreateIndex("IDX_AIToolInstance_Source", "DocumentId", nameof(AIToolInstanceIndex.Source)),
            collection: options?.AICollectionName);
    }
}
