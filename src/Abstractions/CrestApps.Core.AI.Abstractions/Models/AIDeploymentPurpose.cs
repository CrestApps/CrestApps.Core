namespace CrestApps.Core.AI.Models;

/// <summary>
/// Flags enumeration defining the purposes supported by an AI deployment.
/// Multiple purposes can be combined using bitwise OR.
/// </summary>
[Flags]
public enum AIDeploymentPurpose
{
    /// <summary>
    /// No purposes are assigned.
    /// </summary>
    None = 0,

    /// <summary>
    /// Chat completion purpose.
    /// </summary>
    Chat = 1 << 0,

    /// <summary>
    /// Utility (non-chat) completion purpose for background tasks such as summarization.
    /// </summary>
    Utility = 1 << 1,

    /// <summary>
    /// Text embedding generation purpose.
    /// </summary>
    Embedding = 1 << 2,

    /// <summary>
    /// Image generation purpose.
    /// </summary>
    Image = 1 << 3,

    /// <summary>
    /// Speech-to-text transcription purpose.
    /// </summary>
    SpeechToText = 1 << 4,

    /// <summary>
    /// Text-to-speech synthesis purpose.
    /// </summary>
    TextToSpeech = 1 << 5,

    /// <summary>
    /// Vision purpose for understanding image inputs in chat-style interactions.
    /// </summary>
    Vision = 1 << 6,
}
