using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

/// <summary>
/// YesSql map index for <see cref="SearchIndexProfile"/>, storing the item identifier,
/// name, provider, index names, and type to support efficient profile queries.
/// </summary>
public sealed class SearchIndexProfileIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the unique technical name of the search index profile.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the name of the search provider that owns this index profile.
    /// </summary>
    public string ProviderName { get; set; }

    /// <summary>
    /// Gets or sets the short index name within the provider.
    /// </summary>
    public string IndexName { get; set; }

    /// <summary>
    /// Gets or sets the fully qualified index name, typically combining the provider prefix
    /// and <see cref="IndexName"/>.
    /// </summary>
    public string IndexFullName { get; set; }

    /// <summary>
    /// Gets or sets the type discriminator for the search index profile.
    /// </summary>
    public string Type { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="SearchIndexProfile"/> documents
/// to <see cref="SearchIndexProfileIndex"/> entries in the default collection.
/// </summary>
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
