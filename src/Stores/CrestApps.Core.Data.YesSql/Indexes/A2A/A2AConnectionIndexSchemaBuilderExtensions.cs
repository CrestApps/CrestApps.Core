using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.A2A;

public static class A2AConnectionIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates 2 a connection index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateA2AConnectionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);

        await schemaBuilder.CreateMapIndexTableAsync<A2AConnectionIndex>(table => table
            .Column<string>(nameof(A2AConnectionIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(A2AConnectionIndex.DisplayText), column => column.WithLength(255)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<A2AConnectionIndex>(table =>
        {
            table.CreateIndex("IDX_A2AConnection_DocumentId", "DocumentId");
        }, collection: options?.AICollectionName);
    }
}
