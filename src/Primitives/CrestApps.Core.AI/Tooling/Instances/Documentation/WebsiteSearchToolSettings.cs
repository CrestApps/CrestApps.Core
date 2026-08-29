namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// The user-provided settings for a live website search tool instance. The instance queries a site's own
/// search API on every request (no crawling or local corpus) and maps the JSON response to documentation
/// results. The defaults target the WordPress REST search endpoint (<c>wp-json/wp/v2/search</c>), so a
/// WordPress site needs only a <see cref="BaseUrl"/>; every field is overridable to target another site's
/// search API.
/// </summary>
public sealed class WebsiteSearchToolSettings
{
    /// <summary>
    /// Gets or sets the base URL of the site (for example <c>https://www.example.com</c>).
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the search endpoint path appended to <see cref="BaseUrl"/>. Defaults to the WordPress
    /// REST search endpoint.
    /// </summary>
    public string SearchPath { get; set; } = "/wp-json/wp/v2/search";

    /// <summary>
    /// Gets or sets the name of the query-string parameter that carries the free-text query the AI model
    /// supplies. Defaults to the WordPress <c>search</c> parameter.
    /// </summary>
    public string QueryParameter { get; set; } = "search";

    /// <summary>
    /// Gets or sets fixed extra query-string parameters always appended to the request (already encoded).
    /// Defaults to <c>_embed=1</c> so the WordPress response embeds each result's post, exposing its
    /// excerpt and content for the snippet.
    /// </summary>
    public string ExtraQuery { get; set; } = "_embed=1";

    /// <summary>
    /// Gets or sets the dotted path to the array of results within the JSON response. Empty means the
    /// response body is itself the array (the WordPress default). Supports property names and array
    /// indices, for example <c>data.hits</c>.
    /// </summary>
    public string ResultsPath { get; set; }

    /// <summary>
    /// Gets or sets the dotted path, relative to each result element, to the result title. Defaults to the
    /// WordPress <c>title</c> field.
    /// </summary>
    public string TitlePath { get; set; } = "title";

    /// <summary>
    /// Gets or sets the dotted path, relative to each result element, to the result URL. Defaults to the
    /// WordPress <c>url</c> field.
    /// </summary>
    public string UrlPath { get; set; } = "url";

    /// <summary>
    /// Gets or sets the dotted path, relative to each result element, to the text snippet. Defaults to the
    /// embedded WordPress post excerpt; point it at <c>_embedded.self[0].content.rendered</c> for the full
    /// body. HTML in the resolved value is stripped.
    /// </summary>
    public string SnippetPath { get; set; } = "_embedded.self[0].excerpt.rendered";

    /// <summary>
    /// Gets or sets the maximum number of results this instance returns for a single search. When not set,
    /// a built-in default is used.
    /// </summary>
    public int? MaxResults { get; set; }
}
