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

        // Pin the response language so the model does not drift to an unrelated language on the first turn.
        // The client always sends the listener's language (their explicit choice, or the browser default).
        var instructions = context.Instructions;
        if (!string.IsNullOrWhiteSpace(context.SpeechLanguage))
        {
            var primaryLanguage = context.SpeechLanguage.Split('-', '_')[0];
            if (!string.IsNullOrWhiteSpace(primaryLanguage))
            {
                var directive = $"Always speak and respond in the user's language ('{primaryLanguage}'). Do not switch to another language unless the user clearly does.";
                instructions = string.IsNullOrWhiteSpace(instructions) ? directive : instructions + "\n\n" + directive;
            }
        }

        return new RealtimeSessionOptions
        {
            Model = context.Model,
            Instructions = instructions,
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
}
