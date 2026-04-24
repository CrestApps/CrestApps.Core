namespace CrestApps.Core.AI;

/// <summary>
/// Identifies an AI provider by its client name. Implement this interface on a
/// marker type so <see cref="Services.ProviderAICompletionClient{TProvider}"/>
/// can resolve the name at compile time.
/// </summary>
public interface IAIClientMarker
{
    /// <summary>
    /// Gets the unique client name that identifies the provider.
    /// </summary>
    static abstract string ClientName { get; }
}
