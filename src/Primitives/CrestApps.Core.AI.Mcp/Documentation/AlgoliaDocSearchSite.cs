namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Describes a documentation site that is searchable through the Algolia DocSearch query API (the
/// hosted search used by many Docusaurus sites). The built-in source forwards queries to Algolia and
/// maps the returned hits to documentation results without crawling or caching a corpus locally.
/// </summary>
public sealed class AlgoliaDocSearchSite
{
    /// <summary>
    /// Gets or sets the unique logical name of the site.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the Algolia application identifier.
    /// </summary>
    public string ApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the Algolia search-only API key.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Algolia index name to query.
    /// </summary>
    public string IndexName { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results this site should contribute to a search. When not
    /// set, the global <see cref="DocumentationSearchOptions.MaxResultsPerSite"/> value is used.
    /// </summary>
    public int? MaxResults { get; set; }
}
