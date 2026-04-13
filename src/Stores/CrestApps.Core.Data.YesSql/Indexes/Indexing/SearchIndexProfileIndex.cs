using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

public sealed class SearchIndexProfileIndex : CatalogItemIndex
{
    public string Name { get; set; }

    public string ProviderName { get; set; }

    public string IndexName { get; set; }

    public string IndexFullName { get; set; }

    public string Type { get; set; }
}

public sealed class SearchIndexProfileIndexProvider : IndexProvider<SearchIndexProfile>
{
    public SearchIndexProfileIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.DefaultCollectionName;
    }

    public override void Describe(DescribeContext<SearchIndexProfile> context)
    {
        context.For<SearchIndexProfileIndex>()
            .Map(profile => new SearchIndexProfileIndex
            {
                ItemId = profile.ItemId,
                Name = profile.Name,
                ProviderName = profile.ProviderName,
                IndexName = profile.IndexName,
                IndexFullName = profile.IndexFullName,
                Type = profile.Type,
            });
    }
}
