namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// The runtime configuration for a live website search source, bound from a
/// <see cref="WebsiteSearchToolSettings"/> instance. Describes the endpoint to call and how to map the
/// JSON response to documentation results.
/// </summary>
public sealed class WebsiteSearchSite
{
    /// <summary>
    /// Gets or sets the unique logical name of the source.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the base URL of the site.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the search endpoint path appended to <see cref="BaseUrl"/>.
    /// </summary>
    public string SearchPath { get; set; }

    /// <summary>
    /// Gets or sets the name of the query-string parameter that carries the free-text query.
    /// </summary>
    public string QueryParameter { get; set; }

    /// <summary>
    /// Gets or sets fixed extra query-string parameters always appended to the request.
    /// </summary>
    public string ExtraQuery { get; set; }

    /// <summary>
    /// Gets or sets the dotted path to the array of results within the JSON response.
    /// </summary>
    public string ResultsPath { get; set; }

    /// <summary>
    /// Gets or sets the dotted path, relative to each result element, to the result title.
    /// </summary>
    public string TitlePath { get; set; }

    /// <summary>
    /// Gets or sets the dotted path, relative to each result element, to the result URL.
    /// </summary>
    public string UrlPath { get; set; }

    /// <summary>
    /// Gets or sets the dotted path, relative to each result element, to the text snippet.
    /// </summary>
    public string SnippetPath { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results this source contributes to a search.
    /// </summary>
    public int? MaxResults { get; set; }
}
