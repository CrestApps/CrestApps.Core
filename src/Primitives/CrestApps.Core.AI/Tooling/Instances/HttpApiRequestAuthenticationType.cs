namespace CrestApps.Core.AI.Tooling.Instances;

/// <summary>
/// Enumerates the authentication strategies supported by the HTTP API request tool.
/// </summary>
public enum HttpApiRequestAuthenticationType
{
    /// <summary>
    /// No authentication is applied to the request.
    /// </summary>
    None = 0,

    /// <summary>
    /// A static API key is sent in a configurable request header.
    /// </summary>
    ApiKey = 1,

    /// <summary>
    /// A bearer token is sent in the <c>Authorization</c> header.
    /// </summary>
    Bearer = 2,

    /// <summary>
    /// HTTP basic authentication (username and password) is applied.
    /// </summary>
    Basic = 3,

    /// <summary>
    /// OAuth 2.0. When a username is configured the resource owner password grant is used; otherwise the
    /// client credentials grant is used. In both cases the tool requests an access token from the
    /// configured token endpoint, caches it on the instance, and reuses a stored refresh token when
    /// available before requesting a new token.
    /// </summary>
    OAuth2 = 4,
}
