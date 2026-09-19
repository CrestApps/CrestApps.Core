namespace CrestApps.Core.AI.OpenAI.Azure;

/// <summary>
/// Marker type that identifies the Azure OpenAI provider for
/// <see cref="Services.ProviderAICompletionClient{TProvider}"/>.
/// </summary>
public readonly struct AzureOpenAIClientMarker : IAIClientMarker
{
    /// <summary>
    /// Gets the client Name.
    /// </summary>
    public static string ClientName => AzureOpenAIConstants.ClientName;
}
