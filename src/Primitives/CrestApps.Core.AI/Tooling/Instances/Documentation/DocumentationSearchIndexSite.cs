namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// Describes a documentation site that publishes a prebuilt search index as JSON (for example a MkDocs
/// Material <c>search_index.json</c>). The built-in source downloads the index once, ranks its entries
/// with keyword scoring, and resolves each result URL relative to <see cref="BaseUrl"/>.
/// </summary>
public sealed class DocumentationSearchIndexSite
{
    /// <summary>
    /// Gets or sets the unique logical name of the site.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the base URL of the documentation site. It is used to resolve relative entry
    /// locations and, when <see cref="IndexUrl"/> is not set, to derive the default index URL.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets an explicit URL to the search index JSON. When not set, the source resolves it from
    /// <see cref="BaseUrl"/> by appending <c>/search/search_index.json</c>.
    /// </summary>
    public string IndexUrl { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results this site should contribute to a search. When not
    /// set, the global <see cref="DocumentationSearchOptions.MaxResultsPerSite"/> value is used.
    /// </summary>
    public int? MaxResults { get; set; }
}
