namespace CrestApps.Core.AI.Completions;

/// <summary>
/// The kinds of AI request that usage metering records in <see cref="Models.AICompletionUsageRecord.OperationType"/>.
/// Each kind is billed in its own units, so reports keep them apart.
/// </summary>
public static class AIUsageOperationTypes
{
    /// <summary>
    /// A chat completion, billed by input, cached input, and output tokens.
    /// </summary>
    public const string Chat = "Chat";

    /// <summary>
    /// An embedding generation, billed by input tokens.
    /// </summary>
    public const string Embedding = "Embedding";

    /// <summary>
    /// An image generation, billed per image or by tokens depending on the model.
    /// </summary>
    public const string Image = "Image";

    /// <summary>
    /// A speech-to-text transcription, billed by audio duration or by tokens depending on the model.
    /// </summary>
    public const string SpeechToText = "SpeechToText";

    /// <summary>
    /// A text-to-speech synthesis, billed by character or by tokens depending on the model.
    /// </summary>
    public const string TextToSpeech = "TextToSpeech";

    /// <summary>
    /// A response produced by a realtime (speech-to-speech) session, billed by text and audio tokens.
    /// </summary>
    public const string Realtime = "Realtime";

    /// <summary>
    /// The transcription of a caller's speech inside a realtime session, billed separately against the
    /// transcription model.
    /// </summary>
    public const string RealtimeTranscription = "RealtimeTranscription";
}
