using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.A2A;

public static class A2AConnectionIndexSchemaBuilderExtensions
{
    public static Task CreateA2AConnectionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, string collection = null)
    {
        return schemaBuilder.CreateMapIndexTableAsync<A2AConnectionIndex>(table => table
            .Column<string>(nameof(A2AConnectionIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(A2AConnectionIndex.DisplayText), column => column.WithLength(255)),
            collection: collection);
    }
}
