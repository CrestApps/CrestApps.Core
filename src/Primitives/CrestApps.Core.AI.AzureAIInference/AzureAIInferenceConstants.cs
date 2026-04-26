namespace CrestApps.Core.AI.AzureAIInference;

/// <summary>
/// Provides functionality for azure AI Inference Constants.
/// </summary>
public static class AzureAIInferenceConstants
{
    public const string ClientName = "AzureAIInference";
}

/// <summary>
/// Marker type that identifies the Azure AI Inference provider for
/// <see cref="Services.ProviderAICompletionClient{TProvider}"/>.
/// </summary>
public readonly struct AzureAIInferenceClientMarker : IAIClientMarker
{
    /// <summary>
    /// Gets the client Name.
    /// </summary>
    public static string ClientName => AzureAIInferenceConstants.ClientName;
}
