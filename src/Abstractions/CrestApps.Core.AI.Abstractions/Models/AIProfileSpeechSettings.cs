namespace CrestApps.Core.AI.Models;

/// <summary>
/// Settings stored on <see cref="AIProfile.Settings"/> that shape how a profile speaks and how it listens. Every
/// member is optional and defaults to the behavior a profile had before these settings existed.
/// </summary>
public sealed class AIProfileSpeechSettings
{
    /// <summary>
    /// Gets or sets how fast the profile speaks, as a multiple of the voice's normal pace (<c>1</c> is normal), or
    /// <see langword="null"/> to use the deployment's own pace. Values are clamped to 0.25-4.
    /// </summary>
    public double? SpeakingRate { get; set; }

    /// <summary>
    /// Gets or sets the pronunciation dictionary applied to text before it is synthesized.
    /// </summary>
    public List<PronunciationEntry> PronunciationEntries { get; set; } = [];

    /// <summary>
    /// Gets or sets the name of the text-to-speech deployment used when the first one fails to synthesize. Empty for none.
    /// </summary>
    public string FallbackTextToSpeechDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the words and phrases the recognizer should favor (product names, jargon). Providers that cannot
    /// bias recognition ignore them.
    /// </summary>
    public List<string> BoostedKeywords { get; set; } = [];

    /// <summary>
    /// Gets or sets the languages the recognizer should expect, as BCP-47 tags. The first one is the primary language.
    /// </summary>
    public List<string> RecognitionLanguages { get; set; } = [];

    /// <summary>
    /// Gets or sets how readily caller speech stops the AI, from <c>0</c> (the AI is never interrupted) to <c>1</c>
    /// (any caller speech interrupts it), or <see langword="null"/> for the default behavior.
    /// </summary>
    public double? InterruptionSensitivity { get; set; }

    /// <summary>
    /// Gets or sets whether the AI makes short acknowledging sounds ("mm-hm") while the caller keeps talking.
    /// </summary>
    public bool EnableBackchanneling { get; set; }

    /// <summary>
    /// Gets or sets the phrases used for backchanneling. When empty, a small built-in set is used.
    /// </summary>
    public List<string> BackchannelPhrases { get; set; } = [];
}
