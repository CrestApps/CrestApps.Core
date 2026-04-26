using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

public static class McpConnectionIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates mcp connection index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateMcpConnectionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<McpConnectionIndex>(table => table
            .Column<string>(nameof(McpConnectionIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(McpConnectionIndex.DisplayText), column => column.WithLength(255))
            .Column<string>(nameof(McpConnectionIndex.Source), column => column.WithLength(50)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<McpConnectionIndex>(table =>
        {
            table.CreateIndex("IDX_McpConnection_DocumentId", "DocumentId");
            table.CreateIndex("IDX_McpConnection_Source", "DocumentId", nameof(McpConnectionIndex.Source));
        }, collection: options?.AICollectionName);
    }
}
