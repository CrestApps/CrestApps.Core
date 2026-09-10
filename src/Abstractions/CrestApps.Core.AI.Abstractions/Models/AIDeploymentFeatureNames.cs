namespace CrestApps.Core.AI.Models;

/// <summary>
/// Well-known technical names of the model features registered by the framework.
/// Modules can register additional features using <see cref="AIDeploymentCapabilityOptions.AddFeature"/>.
/// </summary>
public static class AIDeploymentFeatureNames
{
    /// <summary>
    /// The model can hold a text conversation (standard chat completions). Enabled by default so that
    /// existing chat deployments keep working; a speech-to-speech-only model should clear this feature so
    /// text-based chat modes are not offered for it.
    /// </summary>
    public const string TextGeneration = "textGeneration";

    /// <summary>
    /// The model can call tools or functions supplied with the request.
    /// </summary>
    public const string ToolCalling = "toolCalling";

    /// <summary>
    /// The model generates text embedding vectors. This is a dedicated embedding endpoint, not a chat
    /// model, so it is opt-in and independent of <see cref="TextGeneration"/>.
    /// </summary>
    public const string TextEmbedding = "textEmbedding";

    /// <summary>
    /// The model transcribes audio into text (a dedicated speech-to-text endpoint such as Whisper).
    /// This is deliberately distinct from <see cref="AudioInput"/>, which means "this chat model accepts
    /// audio inline" rather than "this is a transcription endpoint".
    /// </summary>
    public const string SpeechToText = "speechToText";

    /// <summary>
    /// The model synthesizes speech from text (a dedicated text-to-speech endpoint). This is deliberately
    /// distinct from <see cref="AudioOutput"/>, which means "this chat model emits audio inline" rather
    /// than "this is a synthesis endpoint".
    /// </summary>
    public const string TextToSpeech = "textToSpeech";

    /// <summary>
    /// The model can return responses that conform to a supplied JSON schema.
    /// </summary>
    public const string StructuredOutputs = "structuredOutputs";

    /// <summary>
    /// The model can stream response updates as they are produced.
    /// </summary>
    public const string Streaming = "streaming";

    /// <summary>
    /// The model performs internal reasoning before producing an answer.
    /// </summary>
    public const string Reasoning = "reasoning";

    /// <summary>
    /// The model can understand image inputs (vision).
    /// </summary>
    public const string ImageInput = "imageInput";

    /// <summary>
    /// The model can generate images.
    /// </summary>
    public const string ImageOutput = "imageOutput";

    /// <summary>
    /// The model accepts audio input.
    /// </summary>
    public const string AudioInput = "audioInput";

    /// <summary>
    /// The model produces audio output.
    /// </summary>
    public const string AudioOutput = "audioOutput";

    /// <summary>
    /// The model can understand video inputs.
    /// </summary>
    public const string VideoInput = "videoInput";

    /// <summary>
    /// The model can generate video.
    /// </summary>
    public const string VideoOutput = "videoOutput";

    /// <summary>
    /// The model supports real-time, bidirectional speech-to-speech sessions. A deployment that declares
    /// this feature is eligible to fill the realtime slot.
    /// </summary>
    public const string Realtime = "realtime";
}
