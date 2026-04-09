using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

public static class SearchIndexProfileIndexSchemaBuilderExtensions
{
    public static Task CreateSearchIndexProfileIndexSchemaAsync(this ISchemaBuilder schemaBuilder)
    {
        return schemaBuilder.CreateMapIndexTableAsync<SearchIndexProfileIndex>(table => table.Column<string>(nameof(SearchIndexProfileIndex.ItemId), column => column.WithLength(26)).Column<string>(nameof(SearchIndexProfileIndex.Name), column => column.WithLength(255)).Column<string>(nameof(SearchIndexProfileIndex.ProviderName), column => column.WithLength(50)).Column<string>(nameof(SearchIndexProfileIndex.Type), column => column.WithLength(50)));
    }
}