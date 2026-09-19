using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.WebCrawlers;

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
