namespace CrestApps.Core.AI.Models;

/// <summary>
/// Flags enumeration defining the capability types supported by an AI deployment.
/// Multiple capabilities can be combined using bitwise OR.
/// </summary>
[Flags]
public enum AIDeploymentType
{
    /// <summary>
    /// No capabilities are assigned.
    /// </summary>
    None = 0,

    /// <summary>
    /// Chat completion capability.
    /// </summary>
    Chat = 1 << 0,

    /// <summary>
    /// Utility (non-chat) completion capability for background tasks such as summarization.
    /// </summary>
    Utility = 1 << 1,

    /// <summary>
    /// Text embedding generation capability.
    /// </summary>
    Embedding = 1 << 2,

    /// <summary>
    /// Image generation capability.
    /// </summary>
    Image = 1 << 3,

    /// <summary>
    /// Speech-to-text transcription capability.
    /// </summary>
    SpeechToText = 1 << 4,

    /// <summary>
    /// Text-to-speech synthesis capability.
    /// </summary>
    TextToSpeech = 1 << 5,
}
