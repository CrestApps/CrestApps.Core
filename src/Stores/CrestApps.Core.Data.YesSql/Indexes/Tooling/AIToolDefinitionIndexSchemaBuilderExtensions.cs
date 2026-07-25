using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Tooling;

/// <summary>
/// Schema builder extensions that create the <see cref="AIToolDefinitionIndex"/> table.
/// </summary>
public static class AIToolDefinitionIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates the AI tool instance index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIToolDefinitionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIToolDefinitionIndex>(table => table
            .Column<string>(nameof(AIToolDefinitionIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIToolDefinitionIndex.DisplayText), column => column.WithLength(255))
            .Column<string>(nameof(AIToolDefinitionIndex.Source), column => column.WithLength(50)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIToolDefinitionIndex>(
            table => table.CreateIndex("IDX_AIToolDefinition_DocumentId", "DocumentId"),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIToolDefinitionIndex>(
            table => table.CreateIndex("IDX_AIToolDefinition_Source", "DocumentId", nameof(AIToolDefinitionIndex.Source)),
            collection: options?.AICollectionName);
    }
}
