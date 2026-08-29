using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.WebCrawlers;

/// <summary>
/// YesSql map index for <see cref="WebCrawlState"/>, keyed by the owning crawler (its source) so the
/// re-index service can efficiently read every recorded page for a crawler.
/// </summary>
public sealed class WebCrawlStateIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the owning crawler identifier (the crawl-state record's source).
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the scraped page URL.
    /// </summary>
    public string Url { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="WebCrawlState"/> documents to <see cref="WebCrawlStateIndex"/>
/// entries in the AI collection.
/// </summary>
public sealed class WebCrawlStateIndexProvider : IndexProvider<WebCrawlState>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlStateIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public WebCrawlStateIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the index mapping.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<WebCrawlState> context)
    {
        context.For<WebCrawlStateIndex>()
            .Map(state => new WebCrawlStateIndex
            {
                ItemId = state.ItemId,
                Source = state.Source,
                Url = state.Url,
            });
    }
}
