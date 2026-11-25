namespace CrestApps.Core.AI.AzureAIInference;

/// <summary>
/// Provides functionality for azure AI Inference Constants.
/// </summary>
public static class AzureAIInferenceConstants
{
    public const string ClientName = "AzureAIInference";

    /// <summary>
    /// The name of the <see cref="System.Net.Http.IHttpClientFactory"/> client the Azure AI Inference SDK clients send
    /// their requests through. Configure a client of this name to give the provider the host's own transport.
    /// </summary>
    public const string HttpClientName = "CrestApps.AzureAIInference";
}
