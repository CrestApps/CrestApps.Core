using System.Diagnostics;
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
/// <summary>
/// Meters every transcription made through a speech-to-text client the AI client factory created, whatever the
/// provider. Both the audio duration and any tokens reported are recorded, because models bill one or the other.
/// The duration is where the last recognized speech ended, measured from the start of the audio.
/// </summary>
internal sealed class AIUsageTrackingSpeechToTextClient : DelegatingSpeechToTextClient
{
    private readonly AIUsageRecorder _recorder;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageTrackingSpeechToTextClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client.</param>
    /// <param name="recorder">The recorder that stores the usage.</param>
    public AIUsageTrackingSpeechToTextClient(
        ISpeechToTextClient innerClient,
        AIUsageRecorder recorder)
        : base(innerClient)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Transcribes the audio and records its usage.
    /// </summary>
    /// <param name="audioSpeechStream">The audio to transcribe.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await base.GetTextAsync(audioSpeechStream, AIUsageLabels.ForProvider(options), cancellationToken);
        stopwatch.Stop();

        if (response is not null)
        {
            await RecordAsync(options, response.ModelId, response.ResponseId, response.Usage, response.EndTime, stopwatch.Elapsed.TotalMilliseconds, false, cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Transcribes the audio as a stream of updates and records its usage once the stream ends.
    /// </summary>
    /// <param name="audioSpeechStream">The audio to transcribe.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var usage = new AIStreamingUsageAccumulator();
        TimeSpan? endTime = null;

        await foreach (var update in base.GetStreamingTextAsync(audioSpeechStream, AIUsageLabels.ForProvider(options), cancellationToken))
        {
            usage.Add(update.ModelId, update.ResponseId, update.Contents);

            if (update.EndTime is { } updateEndTime && (endTime is null || updateEndTime > endTime))
            {
                endTime = updateEndTime;
            }

            yield return update;
        }

        stopwatch.Stop();

        if (usage.HasUpdates)
        {
            await RecordAsync(options, usage.ModelId, usage.ResponseId, usage.Usage, endTime, stopwatch.Elapsed.TotalMilliseconds, true, cancellationToken);
        }
    }

    private Task RecordAsync(
        SpeechToTextOptions options,
        string modelId,
        string responseId,
        UsageDetails usage,
        TimeSpan? endTime,
        double responseLatencyMs,
        bool isStreaming,
        CancellationToken cancellationToken)
    {
        var audioDurationMs = endTime is { } duration && duration > TimeSpan.Zero
            ? (long)duration.TotalMilliseconds
            : 0;

        return _recorder.RecordAsync(
            AIUsageOperationTypes.SpeechToText,
            options?.AdditionalProperties,
            modelId,
            responseId,
            usage,
            responseLatencyMs,
            isStreaming,
            record => record.AudioDurationMs = record.AudioDurationMs > 0 ? record.AudioDurationMs : audioDurationMs,
            cancellationToken);
    }
}
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
