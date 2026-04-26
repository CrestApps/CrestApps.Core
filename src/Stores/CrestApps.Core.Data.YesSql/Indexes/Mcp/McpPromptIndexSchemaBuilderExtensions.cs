using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

public static class McpPromptIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates mcp prompt index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateMcpPromptIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);

        await schemaBuilder.CreateMapIndexTableAsync<McpPromptIndex>(table => table
            .Column<string>(nameof(McpPromptIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(McpPromptIndex.Name), column => column.WithLength(255)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<McpPromptIndex>(table =>
        {
            table.CreateIndex("IDX_McpPrompt_DocumentId", "DocumentId", nameof(McpPromptIndex.Name));
        }, collection: options?.AICollectionName);
    }
}
