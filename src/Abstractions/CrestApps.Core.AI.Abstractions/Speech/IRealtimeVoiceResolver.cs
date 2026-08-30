using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Speech;

/// <summary>
/// Resolves the available real-time voices for a deployment by delegating to the matching AI client provider.
/// Mirrors <see cref="ISpeechVoiceResolver"/> for the realtime (speech-to-speech) path.
/// </summary>
public interface IRealtimeVoiceResolver
{
    /// <summary>
    /// Gets the available real-time voices for the specified deployment, or an empty array when the deployment's
    /// provider does not support real-time sessions.
    /// </summary>
    /// <param name="deployment">The AI deployment containing provider, connection, and model information.</param>
    Task<SpeechVoice[]> GetVoicesAsync(AIDeployment deployment);
}
