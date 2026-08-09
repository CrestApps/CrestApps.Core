namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Options that control the built-in documentation search sources. Configure sites in code through
/// the documentation search builder, or bind this type from configuration to declare the public
/// documentation sites the search tool is allowed to scan.
/// </summary>
public sealed class DocumentationSearchOptions
{
    /// <summary>
    /// Gets the collection of public documentation sites that the built-in crawler scans.
    /// </summary>
    public IList<DocumentationSite> Sites { get; } = [];

    /// <summary>
    /// Gets the collection of documentation sites that expose a prebuilt search index as JSON (for
    /// example MkDocs Material) that the built-in search-index source downloads and ranks.
    /// </summary>
    public IList<DocumentationSearchIndexSite> SearchIndexes { get; } = [];

    /// <summary>
    /// Gets the collection of documentation sites that are searchable through the Algolia DocSearch
    /// query API.
    /// </summary>
    public IList<AlgoliaDocSearchSite> AlgoliaSources { get; } = [];

    /// <summary>
    /// Gets or sets the default maximum number of results a single site contributes to a search.
    /// </summary>
    public int MaxResultsPerSite { get; set; } = 5;

    /// <summary>
    /// Gets or sets the default maximum number of pages the crawler indexes per site.
    /// </summary>
    public int MaxPagesPerSite { get; set; } = 200;

    /// <summary>
    /// Gets or sets the maximum number of concurrent page requests the crawler issues per site.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>
    /// Gets or sets how long a crawled site corpus is cached before it is refreshed.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(1);
}
