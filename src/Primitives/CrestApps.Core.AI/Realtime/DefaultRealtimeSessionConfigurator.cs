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

        return new RealtimeSessionOptions
        {
            Model = context.Model,
            Instructions = context.Instructions,
            Voice = context.Voice,
            MaxOutputTokens = context.MaxOutputTokens,
            InputAudioFormat = new RealtimeAudioFormat("audio/pcm", context.InputSampleRate),
            OutputAudioFormat = new RealtimeAudioFormat("audio/pcm", context.OutputSampleRate),

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
