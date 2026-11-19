namespace CrestApps.Core.AI.Tooling.Instances;

/// <summary>
/// Cached OAuth 2.0 token state persisted on an <see cref="AIToolInstance"/> so the HTTP tool can reuse a
/// valid access token across requests and refresh it without re-authenticating on every call. The access
/// and refresh tokens are data-protected at rest.
/// </summary>
/// <remarks>
/// The state records the base URL and token endpoint the tokens were issued for. The tool ignores a state
/// whose addresses do not match the instance's current settings, so a token is never sent to a host, or a
/// refresh token posted to an endpoint, it was not issued for.
/// </remarks>
public sealed class HttpApiRequestTokenState
{
    /// <summary>
    /// Gets or sets the data-protected access token.
    /// </summary>
    public string AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the data-protected refresh token, when the provider returned one.
    /// </summary>
    public string RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the token type returned by the provider (for example, <c>Bearer</c>).
    /// </summary>
    public string TokenType { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which the cached access token expires.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the base URL the access token was obtained for. A state whose base URL differs from
    /// <see cref="HttpApiRequestToolSettings.BaseUrl"/> is ignored.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the token endpoint that issued the tokens. A state whose token endpoint differs from
    /// <see cref="HttpApiRequestToolSettings.TokenEndpoint"/> is ignored, so its refresh token is never
    /// posted to another endpoint.
    /// </summary>
    public string TokenEndpoint { get; set; }
}
