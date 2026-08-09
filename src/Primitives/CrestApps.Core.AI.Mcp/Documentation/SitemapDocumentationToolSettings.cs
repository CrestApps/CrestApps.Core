namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// The user-provided settings for a sitemap crawling documentation search tool instance. The settings
/// are persisted in the owning <see cref="CrestApps.Core.AI.Tooling.AIToolInstance.Properties"/> and bind
/// the produced function to a single documentation site whose pages are discovered through its
/// <c>sitemap.xml</c>.
/// </summary>
public sealed class SitemapDocumentationToolSettings
{
    /// <summary>
    /// Gets or sets the base URL of the documentation site (for example <c>https://core.crestapps.com</c>).
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets an explicit sitemap URL. When not set, the crawler resolves the sitemap from
    /// <see cref="BaseUrl"/> by appending <c>/sitemap.xml</c>.
    /// </summary>
    public string SitemapUrl { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results this instance returns for a single search. When not
    /// set, a built-in default is used.
    /// </summary>
    public int? MaxResults { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pages the crawler indexes for this site. When not set, a
    /// built-in default is used.
    /// </summary>
    public int? MaxPages { get; set; }
}
