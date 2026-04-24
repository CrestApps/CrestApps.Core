namespace CrestApps.Core.AI.OpenAI;

public static class OpenAIConstants
{
    public const string ClientName = "OpenAI";
}

/// <summary>
/// Marker type that identifies the OpenAI provider for
/// <see cref="Services.ProviderAICompletionClient{TProvider}"/>.
/// </summary>
public readonly struct OpenAIClientMarker : IAIClientMarker
{
    /// <inheritdoc />
    public static string ClientName => OpenAIConstants.ClientName;
}
