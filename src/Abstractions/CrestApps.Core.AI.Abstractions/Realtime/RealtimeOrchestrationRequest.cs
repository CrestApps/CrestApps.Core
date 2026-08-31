using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Describes a realtime session to start.
/// </summary>
public sealed class RealtimeOrchestrationRequest
{
    /// <summary>Gets the resource (an <see cref="AIProfile"/> or <see cref="ChatInteraction"/>) that drives the session.</summary>
    public required object Resource { get; init; }

    /// <summary>Gets the realtime deployment name to use, or <see langword="null"/> to resolve the default realtime deployment.</summary>
    public string RealtimeDeploymentName { get; init; }

    /// <summary>Gets the chat session this conversation is attached to, when persisting transcript history.</summary>
    public AIChatSession ChatSession { get; init; }

    /// <summary>Gets the chat interaction this conversation is attached to, when applicable.</summary>
    public ChatInteraction Interaction { get; init; }

    /// <summary>Gets an optional voice id override for the session output audio.</summary>
    public string Voice { get; init; }

    /// <summary>Gets an optional BCP-47 language hint for input-audio transcription.</summary>
    public string SpeechLanguage { get; init; }

    /// <summary>Gets an optional delegate to further configure the prepared orchestration context.</summary>
    public Action<OrchestrationContext> ConfigureContext { get; init; }
}
