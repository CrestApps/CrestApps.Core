using CrestApps.Core.Infrastructure.Indexing.Models;

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
