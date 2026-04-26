namespace CrestApps.Core.AI.Ollama;

/// <summary>
/// Provides functionality for ollama Constants.
/// </summary>
public static class OllamaConstants
{
    public const string ClientName = "Ollama";
}

/// <summary>
/// Marker type that identifies the Ollama provider for
/// <see cref="Services.ProviderAICompletionClient{TProvider}"/>.
/// </summary>
public readonly struct OllamaClientMarker : IAIClientMarker
{
    /// <summary>
    /// Gets the client Name.
    /// </summary>
    /// <inheritdoc />
    public static string ClientName => OllamaConstants.ClientName;
}
