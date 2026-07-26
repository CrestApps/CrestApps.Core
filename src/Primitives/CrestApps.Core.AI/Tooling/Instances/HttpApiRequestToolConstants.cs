namespace CrestApps.Core.AI.Tooling.Instances;

/// <summary>
/// Well-known identifiers for the built-in HTTP API request tool instance source.
/// </summary>
public static class HttpApiRequestToolConstants
{
    /// <summary>
    /// The registered source name. Instances created from this source store this value as their source.
    /// </summary>
    public const string SourceName = "http-api-request";

    /// <summary>
    /// The data-protection purpose used to protect and unprotect stored credentials.
    /// </summary>
    public const string DataProtectionPurpose = "CrestApps.Core.AI.Tooling.HttpApiRequest";

    /// <summary>
    /// The named <see cref="System.Net.Http.HttpClient"/> used to issue requests.
    /// </summary>
    public const string HttpClientName = "CrestApps.Core.AI.HttpApiRequest";
}
