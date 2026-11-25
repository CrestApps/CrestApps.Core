namespace CrestApps.Core.AI.Ollama;

/// <summary>
/// Provides functionality for ollama Constants.
/// </summary>
public static class OllamaConstants
{
    public const string ClientName = "Ollama";

    /// <summary>
    /// The name of the <see cref="System.Net.Http.IHttpClientFactory"/> client the Ollama client sends its requests
    /// through. Configure a client of this name to give the provider the host's own transport.
    /// </summary>
    public const string HttpClientName = "CrestApps.Ollama";
}
