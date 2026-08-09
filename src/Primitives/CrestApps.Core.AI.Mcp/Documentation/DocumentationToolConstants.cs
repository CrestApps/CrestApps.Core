namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Well-known identifiers for the built-in documentation search tool instance sources. Each source is
/// registered under a unique name that is stored as the <see cref="CrestApps.Core.Models.SourceCatalogEntry.Source"/>
/// of every <see cref="CrestApps.Core.AI.Tooling.AIToolInstance"/> created from it.
/// </summary>
public static class DocumentationToolConstants
{
    /// <summary>
    /// The registered source name of the sitemap crawling documentation source (for example a public
    /// Docusaurus site that exposes a <c>sitemap.xml</c>).
    /// </summary>
    public const string SitemapSourceName = "sitemap-documentation";

    /// <summary>
    /// The registered source name of the prebuilt JSON search index documentation source (for example a
    /// MkDocs Material <c>search_index.json</c>).
    /// </summary>
    public const string SearchIndexSourceName = "search-index-documentation";

    /// <summary>
    /// The registered source name of the hosted Algolia DocSearch documentation source.
    /// </summary>
    public const string AlgoliaSourceName = "algolia-documentation";

    /// <summary>
    /// The category applied to the documentation search sources so they are grouped as knowledge-base tools.
    /// </summary>
    public const string Category = "Knowledgebase";
}
