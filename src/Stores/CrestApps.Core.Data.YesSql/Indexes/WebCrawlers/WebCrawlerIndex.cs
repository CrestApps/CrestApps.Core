using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.WebCrawlers;

/// <summary>
/// YesSql map index for <see cref="WebCrawler"/>, keyed by the target data source so the source handler
/// and re-index service can find every crawler for a <c>Web</c> data source.
/// </summary>
public sealed class WebCrawlerIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the human-readable display text of the crawler.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the target AI data source identifier.
    /// </summary>
    public string AIDataSourceId { get; set; }

    /// <summary>
    /// Gets or sets the crawl strategy identifier (the crawler's source).
    /// </summary>
    public string Source { get; set; }
}

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
