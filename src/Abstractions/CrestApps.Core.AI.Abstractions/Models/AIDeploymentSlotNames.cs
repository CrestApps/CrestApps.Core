namespace CrestApps.Core.AI.Models;

/// <summary>
/// Well-known technical names of the deployment slots registered by the framework.
/// </summary>
/// <remarks>
/// A slot is a named role this installation uses a deployment for. It is not a capability: the capability
/// says what the model can do, the slot says where a capable deployment gets used. Modules can register
/// additional slots.
/// </remarks>
public static class AIDeploymentSlotNames
{
    /// <summary>
    /// The deployment that serves interactive text chat.
    /// </summary>
    public const string Chat = "chat";

    /// <summary>
    /// The deployment that serves background work such as summarization, data extraction, and query
    /// rewriting. Falls back to the <see cref="Chat"/> slot.
    /// </summary>
    public const string Utility = "utility";

    /// <summary>
    /// The deployment that generates text embedding vectors.
    /// </summary>
    public const string Embedding = "embedding";

    /// <summary>
    /// The deployment that generates images.
    /// </summary>
    public const string Image = "image";

    /// <summary>
    /// The deployment that understands image inputs.
    /// </summary>
    public const string Vision = "vision";

    /// <summary>
    /// The deployment that transcribes audio into text.
    /// </summary>
    public const string SpeechToText = "speechToText";

    /// <summary>
    /// The deployment that synthesizes speech from text.
    /// </summary>
    public const string TextToSpeech = "textToSpeech";

    /// <summary>
    /// The deployment that runs realtime (speech-to-speech) sessions.
    /// </summary>
    public const string Realtime = "realtime";
}
