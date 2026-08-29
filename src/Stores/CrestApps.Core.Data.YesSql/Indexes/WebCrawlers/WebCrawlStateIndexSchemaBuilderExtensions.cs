using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.WebCrawlers;

/// <summary>
/// Schema builder for the <see cref="WebCrawlStateIndex"/> table.
/// </summary>
public static class WebCrawlStateIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates the web crawl-state index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateWebCrawlStateIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<WebCrawlStateIndex>(table => table
            .Column<string>(nameof(WebCrawlStateIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(WebCrawlStateIndex.Source), column => column.WithLength(26))
            .Column<string>(nameof(WebCrawlStateIndex.Url), column => column.WithLength(2048)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<WebCrawlStateIndex>(table => table
            .CreateIndex("IDX_WebCrawlState_Source", "DocumentId", nameof(WebCrawlStateIndex.Source)),
            collection: options?.AICollectionName);
    }
}
