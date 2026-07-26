namespace CrestApps.Core.AI.Tooling.Instances;

/// <summary>
/// The user-provided settings that configure a single HTTP API request tool instance. These values are
/// captured up front by the user (not by the AI model) and are persisted in the instance's properties.
/// </summary>
public sealed class HttpApiRequestToolSettings
{
    /// <summary>
    /// Gets or sets the base URL the request targets. The model may append a relative path when
    /// <see cref="AllowModelProvidedPath"/> is enabled.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the HTTP method to use (for example, <c>GET</c>, <c>POST</c>, <c>PUT</c>,
    /// <c>PATCH</c>, or <c>DELETE</c>). Defaults to <c>GET</c>.
    /// </summary>
    public string HttpMethod { get; set; } = "GET";

    /// <summary>
    /// Gets or sets the authentication strategy applied to the request.
    /// </summary>
    public HttpApiRequestAuthenticationType AuthenticationType { get; set; }

    /// <summary>
    /// Gets or sets the header name used when <see cref="AuthenticationType"/> is
    /// <see cref="HttpApiRequestAuthenticationType.ApiKey"/>. Defaults to <c>X-Api-Key</c>.
    /// </summary>
    public string ApiKeyHeaderName { get; set; }

    /// <summary>
    /// Gets or sets the API key value used for API key authentication. May be data-protected at rest.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the bearer token used for bearer authentication. May be data-protected at rest.
    /// </summary>
    public string BearerToken { get; set; }

    /// <summary>
    /// Gets or sets the username used for basic authentication.
    /// </summary>
    public string BasicUsername { get; set; }

    /// <summary>
    /// Gets or sets the password used for basic authentication. May be data-protected at rest.
    /// </summary>
    public string BasicPassword { get; set; }

    /// <summary>
    /// Gets or sets the OAuth 2.0 token endpoint the tool requests access tokens from when
    /// <see cref="AuthenticationType"/> is <see cref="HttpApiRequestAuthenticationType.OAuth2"/>.
    /// </summary>
    public string TokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the OAuth 2.0 client identifier.
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the OAuth 2.0 client secret. May be data-protected at rest.
    /// </summary>
    public string ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the optional OAuth 2.0 scope requested when acquiring an access token.
    /// </summary>
    public string Scope { get; set; }

    /// <summary>
    /// Gets or sets static headers that are always added to the request.
    /// </summary>
    public Dictionary<string, string> DefaultHeaders { get; set; }

    /// <summary>
    /// Gets or sets whether the AI model may supply a relative path appended to <see cref="BaseUrl"/>.
    /// </summary>
    public bool AllowModelProvidedPath { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the AI model may supply query string parameters.
    /// </summary>
    public bool AllowModelProvidedQuery { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the AI model may supply a request body (for methods that support one).
    /// </summary>
    public bool AllowModelProvidedBody { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional per-request timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; }
}
