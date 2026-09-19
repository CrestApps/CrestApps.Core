namespace CrestApps.Core.AI.OpenAI;

/// <summary>
/// Marker type that identifies the OpenAI provider for
/// <see cref="Services.ProviderAICompletionClient{TProvider}"/>.
/// </summary>
public readonly struct OpenAIClientMarker : IAIClientMarker
{
    /// <summary>
    /// Gets the client Name.
    /// </summary>
    public static string ClientName => OpenAIConstants.ClientName;
}
