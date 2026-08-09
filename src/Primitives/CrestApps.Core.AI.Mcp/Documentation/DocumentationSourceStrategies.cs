namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// The well-known documentation search strategy identifiers. A strategy determines how a stored
/// <see cref="DocumentationSourceEntry"/> is materialized into a runtime <see cref="IDocumentationSource"/>.
/// The value is stored in <see cref="DocumentationSourceEntry.Source"/> so the catalog can carry
/// heterogeneous strategies in a single store. Custom strategies can be added by registering an
/// <see cref="IDocumentationSourceFactory"/> with a new strategy identifier.
/// </summary>
public static class DocumentationSourceStrategies
{
    /// <summary>
    /// The strategy that crawls a site's <c>sitemap.xml</c> and ranks pages locally.
    /// </summary>
    public const string Sitemap = "sitemap";

    /// <summary>
    /// The strategy that downloads a prebuilt JSON search index (for example a MkDocs Material
    /// <c>search_index.json</c>) and ranks its entries locally.
    /// </summary>
    public const string SearchIndex = "search-index";

    /// <summary>
    /// The strategy that forwards queries to the hosted Algolia DocSearch query API.
    /// </summary>
    public const string Algolia = "algolia";
}
