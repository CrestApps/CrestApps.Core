using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.OpenAI;

/// <summary>
/// The voices supported by the OpenAI (and Azure OpenAI) <c>gpt-realtime</c> family. The realtime API validates the
/// session voice against this fixed, model-defined set; there is no API to enumerate it, so it is declared here and
/// surfaced through <c>IAIClientProvider.GetRealtimeVoicesAsync</c>.
/// </summary>
/// <remarks>
/// OpenAI does not publish an official gender for these voices, and some (for example <c>alloy</c>) are intentionally
/// neutral. The <see cref="SpeechVoice.Gender"/> values below are a best-effort, community-perceived classification
/// intended only to help group the selector — treat them as approximate, not authoritative.
/// </remarks>
public static class OpenAIRealtimeVoices
{
    /// <summary>
    /// Gets the available realtime voices, in alphabetical order.
    /// </summary>
    public static SpeechVoice[] All { get; } =
    [
        Voice("alloy", SpeechVoiceGender.Neutral),
        Voice("ash", SpeechVoiceGender.Male),
        Voice("ballad", SpeechVoiceGender.Male),
        Voice("cedar", SpeechVoiceGender.Male),
        Voice("coral", SpeechVoiceGender.Female),
        Voice("echo", SpeechVoiceGender.Male),
        Voice("marin", SpeechVoiceGender.Female),
        Voice("sage", SpeechVoiceGender.Female),
        Voice("shimmer", SpeechVoiceGender.Female),
        Voice("verse", SpeechVoiceGender.Male),
    ];

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
