using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.WebCrawlers;

/// <summary>
/// Schema builder for the <see cref="WebCrawlerIndex"/> table.
/// </summary>
public static class WebCrawlerIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates the web-crawler index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateWebCrawlerIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<WebCrawlerIndex>(table => table
            .Column<string>(nameof(WebCrawlerIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(WebCrawlerIndex.DisplayText), column => column.WithLength(255))
            .Column<string>(nameof(WebCrawlerIndex.AIDataSourceId), column => column.WithLength(26))
            .Column<string>(nameof(WebCrawlerIndex.Source), column => column.WithLength(128)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<WebCrawlerIndex>(table => table
            .CreateIndex("IDX_WebCrawler_DataSourceId", "DocumentId", nameof(WebCrawlerIndex.AIDataSourceId)),
            collection: options?.AICollectionName);
    }
}
