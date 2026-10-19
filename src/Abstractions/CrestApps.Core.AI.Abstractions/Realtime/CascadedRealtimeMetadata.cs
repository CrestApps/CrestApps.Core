using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Metadata stored on an <see cref="AIDeployment"/> that turns it into a cascaded realtime deployment:
/// one that answers speech with speech by chaining a realtime speech-to-text deployment, a chat
/// deployment, and a text-to-speech deployment together.
/// </summary>
/// <remarks>
/// Most providers do not ship a speech-to-speech model. A cascaded deployment lets those providers serve
/// the realtime experience anyway, and lets a host mix vendors — transcribing with one, reasoning with
/// another, and speaking with a third. The deployment carrying this metadata still has to declare the
/// <see cref="AIDeploymentFeatureNames.Realtime"/> feature so the realtime orchestrator resolves it.
/// </remarks>
public sealed class CascadedRealtimeMetadata
{
    /// <summary>
    /// Gets or sets the technical name of the deployment that transcribes the user's speech. The deployment
    /// must expose a realtime client capable of a transcription session.
    /// </summary>
    public string SpeechToTextDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the technical name of the chat deployment that generates the reply. Tools, data sources,
    /// and instructions resolved by the orchestrator are applied to this deployment.
    /// </summary>
    public string ChatDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the technical name of the deployment that speaks the reply.
    /// </summary>
    public string TextToSpeechDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets how fast the cascade speaks, as a multiple of its text-to-speech deployment's normal pace, or
    /// <see langword="null"/> for that pace.
    /// </summary>
    /// <remarks>
    /// A phone call has no listener-side control, so whatever pace it should have comes from here. A listener's own
    /// speaking speed applies on top of it: at 1.2 here, a listener's 1.25x speaks at 1.5. Each provider keeps the
    /// result within what it accepts (0.25 to 4 for OpenAI, 0.5 to 2 for Azure Speech).
    /// </remarks>
    public double? SpeechSpeed { get; set; }

    /// <summary>
    /// Gets or sets how the cascade should speak, in words, for a text-to-speech model that takes instructions
    /// (OpenAI's <c>gpt-4o-mini-tts</c>, for example), or <see langword="null"/> for none.
    /// </summary>
    /// <remarks>
    /// For example: "Speak at a brisk, natural phone pace, like a friendly and efficient receptionist." A model that
    /// takes no instructions speaks without them.
    /// </remarks>
    public string SpeakingStyle { get; set; }

    /// <summary>
    /// Determines whether every deployment the cascade needs has been named.
    /// </summary>
    public bool IsComplete()
    {
        return !string.IsNullOrWhiteSpace(SpeechToTextDeploymentName)
            && !string.IsNullOrWhiteSpace(ChatDeploymentName)
            && !string.IsNullOrWhiteSpace(TextToSpeechDeploymentName);
    }
}
