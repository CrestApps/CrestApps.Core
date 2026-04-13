using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

public static class McpConnectionIndexSchemaBuilderExtensions
{
    public static Task CreateMcpConnectionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options = null)
    {
        options ??= new YesSqlStoreOptions();

        return schemaBuilder.CreateMapIndexTableAsync<McpConnectionIndex>(table => table
            .Column<string>(nameof(McpConnectionIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(McpConnectionIndex.DisplayText), column => column.WithLength(255))
            .Column<string>(nameof(McpConnectionIndex.Source), column => column.WithLength(50)),
            collection: options.AICollectionName);
    }
}
