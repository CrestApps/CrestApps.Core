using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.WebCrawlers;

/// <summary>
/// YesSql index provider that maps <see cref="WebCrawler"/> documents to <see cref="WebCrawlerIndex"/>
/// entries in the AI collection.
/// </summary>
public sealed class WebCrawlerIndexProvider : IndexProvider<WebCrawler>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlerIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public WebCrawlerIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the index mapping.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<WebCrawler> context)
    {
        context.For<WebCrawlerIndex>()
            .Map(crawler => new WebCrawlerIndex
            {
                ItemId = crawler.ItemId,
                DisplayText = crawler.DisplayText,
                AIDataSourceId = crawler.AIDataSourceId,
                Source = crawler.Source,
            });
    }
}
