using System.Text.Json.Serialization;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// A catalog entry that describes a documentation search source stored in a catalog (for example a
/// YesSql or EntityCore store) so sources can be created and managed through a UI or database in
/// addition to being registered in code. The <see cref="SourceCatalogEntry.Source"/> value carries the
/// search strategy (see <see cref="DocumentationSourceStrategies"/>), and the remaining properties hold
/// the union of settings used by the built-in strategies.
/// </summary>
public sealed class DocumentationSourceEntry : SourceCatalogEntry, INameAwareModel, IModifiedUtcAwareModel, ICloneable<DocumentationSourceEntry>
{
    /// <summary>
    /// Gets or sets the unique logical name of the source. A caller can scope a search to this name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the search strategy identifier. This is an alias for
    /// <see cref="SourceCatalogEntry.Source"/> and must match a registered
    /// <see cref="IDocumentationSourceFactory.Strategy"/> (see <see cref="DocumentationSourceStrategies"/>).
    /// </summary>
    [JsonIgnore]
    public string Strategy
    {
        get => Source;
        set => Source = value;
    }

    /// <summary>
    /// Gets or sets the human-readable display name of the source.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the base URL of the documentation site. Used by the sitemap and search-index
    /// strategies to resolve the sitemap, index, and result URLs.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets an explicit sitemap URL for the sitemap strategy. When not set, the sitemap
    /// strategy resolves it from <see cref="BaseUrl"/>.
    /// </summary>
    public string SitemapUrl { get; set; }

    /// <summary>
    /// Gets or sets an explicit search index URL for the search-index strategy. When not set, the
    /// search-index strategy resolves it from <see cref="BaseUrl"/>.
    /// </summary>
    public string IndexUrl { get; set; }

    /// <summary>
    /// Gets or sets the Algolia application identifier for the Algolia strategy.
    /// </summary>
    public string ApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the Algolia search-only API key for the Algolia strategy.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Algolia index name for the Algolia strategy.
    /// </summary>
    public string IndexName { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results this source contributes to a search. When not set,
    /// the global <see cref="DocumentationSearchOptions.MaxResultsPerSite"/> value is used.
    /// </summary>
    public int? MaxResults { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pages the sitemap strategy indexes. When not set, the global
    /// <see cref="DocumentationSearchOptions.MaxPagesPerSite"/> value is used.
    /// </summary>
    public int? MaxPages { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this entry was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this entry was last modified.
    /// </summary>
    public DateTime? ModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who created this entry.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the owner identifier associated with this entry.
    /// </summary>
    public string OwnerId { get; set; }

    /// <summary>
    /// Creates a deep copy of this entry.
    /// </summary>
    /// <returns>The cloned entry.</returns>
    public DocumentationSourceEntry Clone()
    {
        return new DocumentationSourceEntry
        {
            ItemId = ItemId,
            Source = Source,
            Name = Name,
            DisplayText = DisplayText,
            BaseUrl = BaseUrl,
            SitemapUrl = SitemapUrl,
            IndexUrl = IndexUrl,
            ApplicationId = ApplicationId,
            ApiKey = ApiKey,
            IndexName = IndexName,
            MaxResults = MaxResults,
            MaxPages = MaxPages,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = ModifiedUtc,
            Author = Author,
            OwnerId = OwnerId,
            Properties = Properties.Clone(),
        };
    }
}
