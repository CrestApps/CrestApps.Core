namespace CrestApps.Core.AI.OpenAI;

/// <summary>
/// Provides functionality for open AI Constants.
/// </summary>
public static class OpenAIConstants
{
    public const string ClientName = "OpenAI";

    /// <summary>
    /// The name of the <see cref="System.Net.Http.IHttpClientFactory"/> client the OpenAI SDK client sends its requests
    /// through. Configure a client of this name to give the provider the host's own transport.
    /// </summary>
    public const string HttpClientName = "CrestApps.OpenAI";
}
