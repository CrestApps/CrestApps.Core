#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default <see cref="IRealtimeSessionConfigurator"/>. Assembles <see cref="RealtimeSessionOptions"/> from
/// the resolved request inputs, applying PCM audio defaults, server turn detection, and
/// input-audio transcription so both sides of the conversation produce a transcript.
/// </summary>
public sealed class DefaultRealtimeSessionConfigurator : IRealtimeSessionConfigurator
{
    // End-of-turn silence (ms) used for server VAD when the profile/user has not tuned turn detection. Longer than
    // the provider default so a natural pause within a sentence does not prematurely end the user's turn.
    private const int DefaultSilenceDurationMs = 800;

    private readonly RealtimeTransportOptions _transportOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultRealtimeSessionConfigurator"/> class with the default
    /// transport options.
    /// </summary>
    public DefaultRealtimeSessionConfigurator()
        : this(options: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultRealtimeSessionConfigurator"/> class.
    /// </summary>
    /// <param name="options">The realtime transport options, which carry the default turn-detection algorithm.</param>
    public DefaultRealtimeSessionConfigurator(IOptions<RealtimeTransportOptions> options)
    {
        _transportOptions = options?.Value ?? new RealtimeTransportOptions();
    }

    /// <inheritdoc />
    public RealtimeSessionOptions Configure(RealtimeSessionConfiguratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tools = context.Tools ?? [];
        var hasTools = tools.Count > 0;

        return new RealtimeSessionOptions
        {
            Model = context.Model,
            Instructions = BuildInstructions(context),
            Voice = context.Voice,
            MaxOutputTokens = context.MaxOutputTokens,
            InputAudioFormat = new RealtimeAudioFormat("audio/pcm", context.InputSampleRate),
            OutputAudioFormat = new RealtimeAudioFormat("audio/pcm", context.OutputSampleRate),

            // The Microsoft.Extensions.AI VAD options cannot express the algorithm, eagerness, silence duration or
            // detection threshold, so they ride RawRepresentationFactory for a provider that supports them (see
            // AzureRealtimeProtocol).
            RawRepresentationFactory = () => BuildTurnDetection(context),

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

    /// <summary>
    /// Builds the turn-detection overrides for a session. Semantic detection is the default: the model decides
    /// when the user has finished, so a pause for thought inside a question does not make the assistant answer
    /// half of it — the single biggest difference between a conversation that feels natural and one that talks
    /// over the user. An explicit silence/threshold tuning implies server VAD, since those knobs mean nothing to
    /// the semantic detector.
    /// </summary>
    public RealtimeTurnDetectionOverrides BuildTurnDetection(RealtimeSessionConfiguratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var type = !string.IsNullOrWhiteSpace(context.TurnDetectionType)
            ? context.TurnDetectionType.Trim()
            : (context.SilenceDurationMs.HasValue || context.VadThreshold.HasValue)
                ? RealtimeTurnDetectionTypes.ServerVad
                : (_transportOptions.TurnDetectionType ?? RealtimeTurnDetectionTypes.SemanticVad).Trim();

        if (string.Equals(type, RealtimeTurnDetectionTypes.SemanticVad, StringComparison.OrdinalIgnoreCase))
        {
            var eagerness = !string.IsNullOrWhiteSpace(context.TurnDetectionEagerness)
                ? context.TurnDetectionEagerness
                : _transportOptions.TurnDetectionEagerness;

            return new RealtimeTurnDetectionOverrides
            {
                Type = RealtimeTurnDetectionTypes.SemanticVad,
                Eagerness = string.IsNullOrWhiteSpace(eagerness) ? null : eagerness.Trim().ToLowerInvariant(),
            };
        }

        return new RealtimeTurnDetectionOverrides
        {
            Type = RealtimeTurnDetectionTypes.ServerVad,
            SilenceDurationMs = context.SilenceDurationMs ?? DefaultSilenceDurationMs,
            Threshold = context.VadThreshold,
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
