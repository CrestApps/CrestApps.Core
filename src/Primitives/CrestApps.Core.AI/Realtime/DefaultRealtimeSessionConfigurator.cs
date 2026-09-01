#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default <see cref="IRealtimeSessionConfigurator"/>. Assembles <see cref="RealtimeSessionOptions"/> from
/// the resolved request inputs, applying PCM audio defaults, server voice-activity turn detection, and
/// input-audio transcription so both sides of the conversation produce a transcript.
/// </summary>
public sealed class DefaultRealtimeSessionConfigurator : IRealtimeSessionConfigurator
{
    /// <inheritdoc />
    public RealtimeSessionOptions Configure(RealtimeSessionConfiguratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tools = context.Tools ?? [];
        var hasTools = tools.Count > 0;

        // The Microsoft.Extensions.AI VAD options cannot express silence duration / detection threshold, so
        // carry them through RawRepresentationFactory for a provider that supports them (see AzureRealtimeProtocol).
        var turnDetectionOverrides = new RealtimeTurnDetectionOverrides
        {
            SilenceDurationMs = context.SilenceDurationMs,
            Threshold = context.VadThreshold,
        };

        return new RealtimeSessionOptions
        {
            Model = context.Model,
            Instructions = BuildInstructions(context),
            Voice = context.Voice,
            MaxOutputTokens = context.MaxOutputTokens,
            InputAudioFormat = new RealtimeAudioFormat("audio/pcm", context.InputSampleRate),
            OutputAudioFormat = new RealtimeAudioFormat("audio/pcm", context.OutputSampleRate),
            RawRepresentationFactory = turnDetectionOverrides.HasValues ? (() => turnDetectionOverrides) : null,

            // The realtime API accepts a single output modality; audio output still emits a text
            // transcript (response.output_audio_transcript.*) which drives the UI.
            OutputModalities = ["audio"],

            VoiceActivityDetection = new VoiceActivityDetectionOptions
            {
                Enabled = true,
                AllowInterruption = context.AllowInterruption,
            },

            // Transcribe the user's input audio so their words also appear in the transcript.
            TranscriptionOptions = string.IsNullOrWhiteSpace(context.InputTranscriptionModel)
                ? null
                : new TranscriptionOptions
                {
                    ModelId = context.InputTranscriptionModel,
                    SpeechLanguage = context.SpeechLanguage,
                },

            Tools = hasTools ? [.. tools] : null,
            ToolMode = hasTools ? ChatToolMode.Auto : null,
        };
    }

    // Realtime models drift into other languages on their own, and the transcription SpeechLanguage hint alone
    // does not pin the spoken reply language. Prepend a directive that locks the language while still letting the
    // user switch it explicitly, so an unprompted switch never happens but "answer me in Spanish" still works.
    private static string BuildInstructions(RealtimeSessionConfiguratorContext context)
    {
        var language = LanguageDisplayName(context.SpeechLanguage);

        if (string.IsNullOrWhiteSpace(language))
        {
            return context.Instructions;
        }

        var directive = $"Always speak and respond in {language}. Do not switch to another language on your own — " +
            "only use a different language if the user explicitly asks you to speak that language.";

        return string.IsNullOrWhiteSpace(context.Instructions)
            ? directive
            : $"{directive}\n\n{context.Instructions}";
    }

    // Maps a BCP-47 language tag (e.g. "en" or "en-US") to its English display name (e.g. "English") for the
    // instruction directive. Falls back to the raw tag when the culture cannot be resolved.
    private static string LanguageDisplayName(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        try
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
            var neutral = culture.IsNeutralCulture ? culture : culture.Parent;

            return string.IsNullOrWhiteSpace(neutral?.EnglishName) ? culture.EnglishName : neutral.EnglishName;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return language;
        }
    }
}
