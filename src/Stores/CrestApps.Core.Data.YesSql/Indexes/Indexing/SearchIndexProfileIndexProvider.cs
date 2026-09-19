using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

/// <summary>
/// YesSql index provider that maps <see cref="SearchIndexProfile"/> documents
/// to <see cref="SearchIndexProfileIndex"/> entries in the default collection.
/// </summary>
public sealed class SearchIndexProfileIndexProvider : IndexProvider<SearchIndexProfile>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexProfileIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public SearchIndexProfileIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.DefaultCollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
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
