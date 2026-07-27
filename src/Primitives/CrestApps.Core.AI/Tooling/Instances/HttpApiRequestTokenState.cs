namespace CrestApps.Core.AI.Tooling.Instances;

/// <summary>
/// Cached OAuth 2.0 token state persisted on an <see cref="AIToolInstance"/> so the HTTP tool can reuse a
/// valid access token across requests and refresh it without re-authenticating on every call. The access
/// and refresh tokens are data-protected at rest.
/// </summary>
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
}
