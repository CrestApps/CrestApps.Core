using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.OpenAI;

/// <summary>
/// The voices of OpenAI's text-to-speech models. Like the realtime voices, they are a fixed, model-defined set with no
/// API to enumerate them, so they are declared here and surfaced through <c>IAIClientProvider.GetSpeechVoicesAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// From OpenAI's text-to-speech guide: <c>gpt-4o-mini-tts</c> speaks with all thirteen voices, while <c>tts-1</c> and
/// <c>tts-1-hd</c> support a smaller set of nine.
/// </para>
/// <para>
/// OpenAI does not publish an official gender for these voices, and some (for example <c>alloy</c>) are intentionally
/// neutral. The <see cref="SpeechVoice.Gender"/> values below are a best-effort, community-perceived classification
/// intended only to help group the selector — treat them as approximate, not authoritative.
/// </para>
/// </remarks>
public static class OpenAITextToSpeechVoices
{
    private static readonly HashSet<string> _firstGenerationVoiceIds = new(StringComparer.Ordinal)
    {
        "alloy", "ash", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer",
    };

    /// <summary>
    /// Gets the voices of <c>gpt-4o-mini-tts</c> and later speech models, in alphabetical order.
    /// </summary>
    public static SpeechVoice[] All { get; } =
    [
        Voice("alloy", SpeechVoiceGender.Neutral),
        Voice("ash", SpeechVoiceGender.Male),
        Voice("ballad", SpeechVoiceGender.Male),
        Voice("cedar", SpeechVoiceGender.Male),
        Voice("coral", SpeechVoiceGender.Female),
        Voice("echo", SpeechVoiceGender.Male),
        Voice("fable", SpeechVoiceGender.Neutral),
        Voice("marin", SpeechVoiceGender.Female),
        Voice("nova", SpeechVoiceGender.Female),
        Voice("onyx", SpeechVoiceGender.Male),
        Voice("sage", SpeechVoiceGender.Female),
        Voice("shimmer", SpeechVoiceGender.Female),
        Voice("verse", SpeechVoiceGender.Male),
    ];

    /// <summary>
    /// Gets the voices of <c>tts-1</c> and <c>tts-1-hd</c>, in alphabetical order.
    /// </summary>
    public static SpeechVoice[] FirstGeneration { get; } = All.Where(voice => _firstGenerationVoiceIds.Contains(voice.Id)).ToArray();

    /// <summary>
    /// Gets the voices a speech model speaks with.
    /// </summary>
    /// <param name="model">The model name, such as <c>gpt-4o-mini-tts</c> or <c>tts-1-hd</c>.</param>
    public static SpeechVoice[] For(string model)
    {
        return model is not null && model.StartsWith("tts-1", StringComparison.OrdinalIgnoreCase)
            ? FirstGeneration
            : All;
    }

    private static SpeechVoice Voice(string id, SpeechVoiceGender gender)
    {
        return new SpeechVoice
        {
            Id = id,
            Name = id,
            Gender = gender,
        };
    }
}
