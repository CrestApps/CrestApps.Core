namespace CrestApps.Core.AI.Completions;

/// <summary>
/// How an audio usage report groups metered speech-to-text and text-to-speech requests. Every grouping keeps the
/// provider, the model, and the kind of request apart.
/// </summary>
public enum AIAudioUsageReportGrouping
{
    /// <summary>
    /// One row per provider, model, and kind of request.
    /// </summary>
    Model,

    /// <summary>
    /// One row per provider, model, kind of request, and voice (<see cref="Models.AICompletionUsageRecord.VoiceId"/>).
    /// </summary>
    ModelAndVoice,

    /// <summary>
    /// One row per provider, model, kind of request, and language (<see cref="Models.AICompletionUsageRecord.Language"/>).
    /// </summary>
    ModelAndLanguage,
}
