using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

public static class McpPromptIndexSchemaBuilderExtensions
{
    public static Task CreateMcpPromptIndexSchemaAsync(this ISchemaBuilder schemaBuilder, string collection = null)
    {
        return schemaBuilder.CreateMapIndexTableAsync<McpPromptIndex>(table => table
            .Column<string>(nameof(McpPromptIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(McpPromptIndex.Name), column => column.WithLength(255)),
            collection: collection);
    }
}
