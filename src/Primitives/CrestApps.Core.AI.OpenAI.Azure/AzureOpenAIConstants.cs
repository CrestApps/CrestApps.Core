namespace CrestApps.Core.AI.OpenAI.Azure;

/// <summary>
/// Provides functionality for azure Open AI Constants.
/// </summary>
public static class AzureOpenAIConstants
{
    public const string ClientName = "Azure";

    public const string AzureSpeechClientName = "AzureSpeech";

    /// <summary>
    /// The name of the <see cref="System.Net.Http.IHttpClientFactory"/> client the Azure OpenAI SDK clients send their
    /// requests through, including requests to the Responses API. Configure a client of this name to give the provider
    /// the host's own transport.
    /// </summary>
    public const string HttpClientName = "CrestApps.AzureOpenAI";
}
