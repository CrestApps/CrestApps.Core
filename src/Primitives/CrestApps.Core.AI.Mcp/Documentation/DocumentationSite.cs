namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Describes a public documentation site that the built-in documentation search crawler can scan.
/// A site is identified by its name and base URL; the crawler discovers pages through the site's
/// <c>sitemap.xml</c> unless an explicit <see cref="SitemapUrl"/> is supplied.
/// </summary>
public sealed class DocumentationSite
{
    /// <summary>
    /// Gets or sets the unique logical name of the site. The documentation search tool uses this
    /// value to let a caller scope a search to a single source.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the base URL of the documentation site (for example <c>https://docs.example.com</c>).
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets an explicit sitemap URL. When not set, the crawler resolves the sitemap from
    /// <see cref="BaseUrl"/> by appending <c>/sitemap.xml</c>.
    /// </summary>
    public string SitemapUrl { get; set; }

    /// <summary>
    /// Gets or sets the documentation generator hint for this site.
    /// </summary>
    public DocumentationSiteKind Kind { get; set; } = DocumentationSiteKind.Auto;

    /// <summary>
    /// Gets or sets the maximum number of results this site should contribute to a search. When not
    /// set, the global <see cref="DocumentationSearchOptions.MaxResultsPerSite"/> value is used.
    /// </summary>
    public int? MaxResults { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pages the crawler indexes for this site. When not set, the
    /// global <see cref="DocumentationSearchOptions.MaxPagesPerSite"/> value is used.
    /// </summary>
    public int? MaxPages { get; set; }
}
