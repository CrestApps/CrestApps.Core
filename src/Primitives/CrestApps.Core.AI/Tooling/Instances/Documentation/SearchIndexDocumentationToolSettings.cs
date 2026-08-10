namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// The user-provided settings for a prebuilt search index documentation search tool instance. The
/// settings are persisted in the owning <see cref="CrestApps.Core.AI.Tooling.AIToolInstance.Properties"/>
/// and bind the produced function to a single documentation site that publishes a JSON search index (for
/// example a MkDocs Material <c>search_index.json</c>).
/// </summary>
public sealed class SearchIndexDocumentationToolSettings
{
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
    /// Gets or sets the maximum number of results this instance returns for a single search. When not
    /// set, a built-in default is used.
    /// </summary>
    public int? MaxResults { get; set; }
}
