using CrestApps.Core.AI;

namespace CrestApps.Core.AI.Ollama;

public static class OllamaConstants
{
    public const string ClientName = "Ollama";
}

/// <summary>
/// Marker type that identifies the Ollama provider for
/// <see cref="Services.ProviderAICompletionClient{TProvider}"/>.
/// </summary>
public readonly struct OllamaProviderMarker : IAIProviderMarker
{
    /// <inheritdoc />
    public static string ClientName => OllamaConstants.ClientName;
}
