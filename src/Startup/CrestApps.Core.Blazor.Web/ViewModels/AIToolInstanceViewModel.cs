using System.ComponentModel.DataAnnotations;
using CrestApps.Core.AI.Tooling.Instances;
using CrestApps.Core.Startup.Shared.ViewModels;

namespace CrestApps.Core.Blazor.Web.ViewModels;

/// <summary>
/// The view model used to create and edit an AI tool instance configured from the built-in HTTP API request source.
/// </summary>
public sealed class AIToolInstanceViewModel
{
    /// <summary>
    /// Gets or sets the identifier of the instance being edited.
    /// </summary>
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or sets the tool source name (the catalog source).
    /// </summary>
    public string Source { get; set; } = HttpApiRequestToolConstants.SourceName;

    /// <summary>
    /// Gets or sets the unique technical name used to derive the function name exposed to the AI model.
    /// </summary>
    [Required]
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the description shown to the AI model so it can distinguish this instance from other instances.
    /// </summary>
    [Required]
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the base URL the request targets.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the optional path appended to the base URL, which may contain {parameter} tokens.
    /// </summary>
    public string PathTemplate { get; set; }

    /// <summary>
    /// Gets or sets the parameters declared for this instance.
    /// </summary>
    public List<AIToolInstanceParameterViewModel> Parameters { get; set; } = [];

    /// <summary>
    /// Gets or sets the HTTP method to use.
    /// </summary>
    public string HttpMethod { get; set; } = "GET";

    /// <summary>
    /// Gets or sets the authentication strategy applied to the request.
    /// </summary>
    public HttpApiRequestAuthenticationType AuthenticationType { get; set; }

    /// <summary>
    /// Gets or sets the header name used for API key authentication.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Gets or sets the API key value used for API key authentication.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a protected API key is already stored.
    /// </summary>
    public bool HasApiKey { get; set; }

    /// <summary>
    /// Gets or sets the bearer token used for bearer authentication.
    /// </summary>
    public string BearerToken { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a protected bearer token is already stored.
    /// </summary>
    public bool HasBearerToken { get; set; }

    /// <summary>
    /// Gets or sets the username used for basic authentication.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets the password used for basic authentication.
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a protected basic password is already stored.
    /// </summary>
    public bool HasPassword { get; set; }

    /// <summary>
    /// Gets or sets the OAuth 2.0 token endpoint used to acquire access tokens.
    /// </summary>
    public string TokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the OAuth 2.0 client identifier.
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the OAuth 2.0 client secret.
    /// </summary>
    public string ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a protected client secret is already stored.
    /// </summary>
    public bool HasClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the optional OAuth 2.0 scope requested when acquiring an access token.
    /// </summary>
    public string Scope { get; set; }

    /// <summary>
    /// Gets or sets the static headers, as a JSON object, always added to the request.
    /// </summary>
    public string DefaultHeaders { get; set; } = "{}";

    /// <summary>
    /// Gets or sets a value indicating whether the AI model may supply a relative path.
    /// </summary>
    public bool AllowModelProvidedPath { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the AI model may supply query string parameters.
    /// </summary>
    public bool AllowModelProvidedQuery { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the AI model may supply a request body.
    /// </summary>
    public bool AllowModelProvidedBody { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional per-request timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the base URL of the documentation site for the sitemap crawling source.
    /// </summary>
    public string SitemapBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets an explicit sitemap URL for the sitemap crawling source. When empty, the crawler
    /// resolves the sitemap from the base URL by appending <c>/sitemap.xml</c>.
    /// </summary>
    public string SitemapUrl { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results the sitemap source returns for a single search.
    /// </summary>
    public int? SitemapMaxResults { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pages the sitemap crawler indexes for the site.
    /// </summary>
    public int? SitemapMaxPages { get; set; }

    /// <summary>
    /// Gets or sets the base URL of the documentation site for the prebuilt search index source.
    /// </summary>
    public string SearchIndexBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets an explicit URL to the search index JSON for the prebuilt search index source. When
    /// empty, the source resolves it from the base URL by appending <c>/search/search_index.json</c>.
    /// </summary>
    public string SearchIndexUrl { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results the prebuilt search index source returns for a single search.
    /// </summary>
    public int? SearchIndexMaxResults { get; set; }

    /// <summary>
    /// Gets or sets the Algolia application identifier for the Algolia DocSearch source.
    /// </summary>
    public string AlgoliaApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the Algolia search-only API key for the Algolia DocSearch source. This is a public,
    /// client-safe key and is stored without additional protection.
    /// </summary>
    public string AlgoliaApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Algolia index name to query for the Algolia DocSearch source.
    /// </summary>
    public string AlgoliaIndexName { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results the Algolia DocSearch source returns for a single search.
    /// </summary>
    public int? AlgoliaMaxResults { get; set; }

    /// <summary>
    /// Gets or sets the base URL of the site for the live website search source.
    /// </summary>
    public string WebsiteSearchBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the search endpoint path for the live website search source. Defaults to the WordPress
    /// REST search endpoint.
    /// </summary>
    public string WebsiteSearchPath { get; set; } = "/wp-json/wp/v2/search";

    /// <summary>
    /// Gets or sets the query-string parameter that carries the model's query for the website search source.
    /// </summary>
    public string WebsiteSearchQueryParameter { get; set; } = "search";

    /// <summary>
    /// Gets or sets fixed extra query-string parameters always appended by the website search source.
    /// </summary>
    public string WebsiteSearchExtraQuery { get; set; } = "_embed=1";

    /// <summary>
    /// Gets or sets the dotted path to the results array within the response for the website search source.
    /// Empty means the response body is itself the array.
    /// </summary>
    public string WebsiteSearchResultsPath { get; set; }

    /// <summary>
    /// Gets or sets the dotted path to each result's title for the website search source.
    /// </summary>
    public string WebsiteSearchTitlePath { get; set; } = "title";

    /// <summary>
    /// Gets or sets the dotted path to each result's URL for the website search source.
    /// </summary>
    public string WebsiteSearchUrlPath { get; set; } = "url";

    /// <summary>
    /// Gets or sets the dotted path to each result's text snippet for the website search source.
    /// </summary>
    public string WebsiteSearchSnippetPath { get; set; } = "_embedded.self[0].excerpt.rendered";

    /// <summary>
    /// Gets or sets the maximum number of results the website search source returns for a single search.
    /// </summary>
    public int? WebsiteSearchMaxResults { get; set; }
}
