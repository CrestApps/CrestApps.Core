using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

public static class McpResourceIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates mcp resource index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateMcpResourceIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<McpResourceIndex>(table => table
            .Column<string>(nameof(McpResourceIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(McpResourceIndex.DisplayText), column => column.WithLength(255))
            .Column<string>(nameof(McpResourceIndex.Source), column => column.WithLength(50)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<McpResourceIndex>(table =>
        {
            table.CreateIndex("IDX_McpResource_DocumentId", "DocumentId");
            table.CreateIndex("IDX_McpResource_Source", "DocumentId", nameof(McpResourceIndex.Source));
        }, collection: options?.AICollectionName);
    }
}
