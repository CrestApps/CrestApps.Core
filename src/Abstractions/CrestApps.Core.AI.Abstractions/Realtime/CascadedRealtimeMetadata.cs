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
    /// Determines whether every deployment the cascade needs has been named.
    /// </summary>
    public bool IsComplete()
    {
        return !string.IsNullOrWhiteSpace(SpeechToTextDeploymentName)
            && !string.IsNullOrWhiteSpace(ChatDeploymentName)
            && !string.IsNullOrWhiteSpace(TextToSpeechDeploymentName);
    }
}
