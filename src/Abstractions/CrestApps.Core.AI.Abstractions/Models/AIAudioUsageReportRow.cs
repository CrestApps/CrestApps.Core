namespace CrestApps.Core.AI.Models;

/// <summary>
/// One row of an audio usage report: the speech-to-text or text-to-speech requests that share a provider, a model,
/// and, depending on the grouping, a voice or a language. Each row sums the units audio models bill by.
/// </summary>
public sealed class AIAudioUsageReportRow
{
    /// <summary>
    /// Gets or sets the provider (client) name.
    /// </summary>
    public string ClientName { get; set; }

    /// <summary>
    /// Gets or sets the model name.
    /// </summary>
    public string ModelName { get; set; }

    /// <summary>
    /// Gets or sets the kind of request, <c>SpeechToText</c> or <c>TextToSpeech</c>.
    /// </summary>
    public string OperationType { get; set; }

    /// <summary>
    /// Gets or sets the voice, when the report is grouped by voice.
    /// </summary>
    public string VoiceId { get; set; }

    /// <summary>
    /// Gets or sets the language, when the report is grouped by language.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets the number of requests.
    /// </summary>
    public int RequestCount { get; set; }

    /// <summary>
    /// Gets or sets the total length of the audio, in milliseconds. Requests whose length could not be determined
    /// add nothing, so see <see cref="RequestsWithDurationCount"/>.
    /// </summary>
    public long AudioDurationMs { get; set; }

    /// <summary>
    /// Gets or sets how many requests reported an audio length, so a total that is short of the real figure can be told
    /// from one that is complete.
    /// </summary>
    public int RequestsWithDurationCount { get; set; }

    /// <summary>
    /// Gets or sets the total length of the speech without the silence before the first word, in milliseconds, for
    /// speech-to-text.
    /// </summary>
    public long SpeechDurationMs { get; set; }

    /// <summary>
    /// Gets the total length of the audio, in hours.
    /// </summary>
    public double AudioHours => AudioDurationMs / 3_600_000d;

    /// <summary>
    /// Gets or sets the number of characters synthesized, for text-to-speech.
    /// </summary>
    public long CharacterCount { get; set; }

    /// <summary>
    /// Gets or sets the size of the audio sent for transcription, in bytes.
    /// </summary>
    public long AudioInputBytes { get; set; }

    /// <summary>
    /// Gets or sets the size of the audio generated, in bytes.
    /// </summary>
    public long AudioOutputBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of text segments transcribed.
    /// </summary>
    public long SegmentCount { get; set; }

    /// <summary>
    /// Gets or sets the input tokens, for models that bill audio by token.
    /// </summary>
    public long InputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the output tokens, for models that bill audio by token.
    /// </summary>
    public long OutputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the total tokens.
    /// </summary>
    public long TotalTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the average response latency in milliseconds.
    /// </summary>
    public double AverageResponseLatencyMs { get; set; }
}
