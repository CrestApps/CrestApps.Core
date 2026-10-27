#pragma warning disable MEAI001 // The realtime and speech APIs from Microsoft.Extensions.AI are for evaluation purposes only.
#nullable enable
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// What a profile changes about a cascaded realtime session: how it speaks, what its recognizer favors, and how
/// readily the caller interrupts it. The orchestrator builds it from the profile's <see cref="AIProfileSpeechSettings"/>.
/// </summary>
public sealed class CascadedRealtimeProfileOptions
{
    /// <summary>
    /// Gets the pronunciation dictionary applied to every spoken fragment.
    /// </summary>
    public PronunciationDictionary? Pronunciation { get; init; }

    /// <summary>
    /// Gets the profile's speaking rate, on top of the deployment's own pace, or <see langword="null"/> for none.
    /// </summary>
    public double? SpeakingRate { get; init; }

    /// <summary>
    /// Gets the client used when the primary text-to-speech client fails before producing audio. The session owns and
    /// disposes it.
    /// </summary>
    public ITextToSpeechClient? FallbackSpeechClient { get; init; }

    /// <summary>
    /// Gets the words and phrases the transcription leg should favor.
    /// </summary>
    public IReadOnlyList<string> BoostedKeywords { get; init; } = [];

    /// <summary>
    /// Gets the language the transcription leg should expect when the session did not name one.
    /// </summary>
    public string? RecognitionLanguage { get; init; }

    /// <summary>
    /// Gets how readily caller speech interrupts the AI, from 0 to 1, or <see langword="null"/> for the default.
    /// </summary>
    public double? InterruptionSensitivity { get; init; }

    /// <summary>
    /// Gets whether the AI acknowledges the caller with short sounds while they keep talking.
    /// </summary>
    public bool EnableBackchanneling { get; init; }

    /// <summary>
    /// Gets the acknowledgement phrases; empty uses the built-in set.
    /// </summary>
    public IReadOnlyList<string> BackchannelPhrases { get; init; } = [];

    /// <summary>
    /// Builds the options from a profile's speech settings.
    /// </summary>
    /// <param name="settings">The profile's speech settings.</param>
    /// <param name="fallbackSpeechClient">The already-created fallback client, if any.</param>
    public static CascadedRealtimeProfileOptions From(AIProfileSpeechSettings settings, ITextToSpeechClient? fallbackSpeechClient)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var dictionary = PronunciationDictionary.Create(settings.PronunciationEntries);

        return new CascadedRealtimeProfileOptions
        {
            Pronunciation = dictionary.IsEmpty ? null : dictionary,
            SpeakingRate = ProfileSpeech.NormalizeSpeakingRate(settings.SpeakingRate),
            FallbackSpeechClient = fallbackSpeechClient,
            BoostedKeywords = ProfileSpeech.Clean(settings.BoostedKeywords),
            RecognitionLanguage = ProfileSpeech.FirstOrDefault(settings.RecognitionLanguages),
            InterruptionSensitivity = settings.InterruptionSensitivity,
            EnableBackchanneling = settings.EnableBackchanneling,
            BackchannelPhrases = ProfileSpeech.Clean(settings.BackchannelPhrases),
        };
    }
}
