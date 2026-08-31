namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

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
    /// The registered source name of the live website search source that queries a site's own search API
    /// (for example the WordPress REST <c>wp-json/wp/v2/search</c> endpoint) instead of crawling it.
    /// </summary>
    public const string WebsiteSearchSourceName = "website-search";

    /// <summary>
    /// The data-protection purpose used to protect and unprotect stored Algolia DocSearch credentials.
    /// </summary>
    public const string AlgoliaDataProtectionPurpose = "CrestApps.Core.AI.Tooling.AlgoliaDocumentation";

    /// <summary>
    /// The category applied to the documentation search sources so they are grouped as knowledge-base tools.
    /// </summary>
    public const string Category = "Knowledgebase";

    /// <summary>
    /// The name of the named <see cref="System.Net.Http.HttpClient"/> used by the documentation search crawlers.
    /// </summary>
    public const string HttpClientName = "CrestApps.Documentation";
}
