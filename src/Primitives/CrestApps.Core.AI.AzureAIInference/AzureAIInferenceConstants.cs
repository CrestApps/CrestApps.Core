namespace CrestApps.Core.AI.AzureAIInference;

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
    /// <inheritdoc />
    public static string ClientName => AzureAIInferenceConstants.ClientName;
}
