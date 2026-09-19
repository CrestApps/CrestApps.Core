namespace CrestApps.Core.AI.Ollama;

/// <summary>
/// Marker type that identifies the Ollama provider for
/// <see cref="Services.ProviderAICompletionClient{TProvider}"/>.
/// </summary>
public readonly struct OllamaClientMarker : IAIClientMarker
{
    /// <summary>
    /// Gets the client Name.
    /// </summary>
    public static string ClientName => OllamaConstants.ClientName;
}
