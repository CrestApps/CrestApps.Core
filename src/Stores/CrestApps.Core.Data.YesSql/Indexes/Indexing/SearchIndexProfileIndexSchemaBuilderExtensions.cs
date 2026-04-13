using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

public static class SearchIndexProfileIndexSchemaBuilderExtensions
{
    public static async Task CreateSearchIndexProfileIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        await schemaBuilder.CreateMapIndexTableAsync<SearchIndexProfileIndex>(table => table
            .Column<string>(nameof(SearchIndexProfileIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(SearchIndexProfileIndex.Name), column => column.WithLength(255))
            .Column<string>(nameof(SearchIndexProfileIndex.ProviderName), column => column.WithLength(255))
            .Column<string>(nameof(SearchIndexProfileIndex.IndexName), column => column.WithLength(255))
            .Column<string>(nameof(SearchIndexProfileIndex.IndexFullName), column => column.WithLength(767))
            .Column<string>(nameof(SearchIndexProfileIndex.Type), column => column.WithLength(50)),
            collection: options?.DefaultCollectionName);

        await schemaBuilder.AlterIndexTableAsync<SearchIndexProfileIndex>(table =>
        {
            table.CreateIndex("IDX_SearchIndexProfile_DocumentId", "DocumentId", nameof(SearchIndexProfileIndex.Name));
            table.CreateIndex("IDX_SearchIndexProfile_Type", "DocumentId", nameof(SearchIndexProfileIndex.Type));
        }, collection: options?.DefaultCollectionName);
    }
}
