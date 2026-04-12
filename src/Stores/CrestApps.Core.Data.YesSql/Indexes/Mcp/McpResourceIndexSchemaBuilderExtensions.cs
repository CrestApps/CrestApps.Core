using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

public static class McpResourceIndexSchemaBuilderExtensions
{
    public static Task CreateMcpResourceIndexSchemaAsync(this ISchemaBuilder schemaBuilder, string collection = null)
    {
        return schemaBuilder.CreateMapIndexTableAsync<McpResourceIndex>(table => table
            .Column<string>(nameof(McpResourceIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(McpResourceIndex.DisplayText), column => column.WithLength(255))
            .Column<string>(nameof(McpResourceIndex.Source), column => column.WithLength(50)),
            collection: collection);
    }
}
