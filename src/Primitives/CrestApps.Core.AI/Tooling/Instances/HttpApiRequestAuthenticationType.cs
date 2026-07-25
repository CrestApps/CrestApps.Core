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
}
