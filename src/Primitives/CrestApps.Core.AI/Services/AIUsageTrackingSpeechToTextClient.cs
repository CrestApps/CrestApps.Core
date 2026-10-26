using System.Diagnostics;
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
/// <summary>
/// Meters every transcription made through a speech-to-text client the AI client factory created, whatever the
/// provider. Both the audio duration and any tokens reported are recorded, because models bill one or the other.
/// The duration is where the last recognized speech ended, measured from the start of the audio, and the start
/// of the first recognized speech is recorded beside it. The request's languages and sample rate, the size of
/// the audio and the number of segments returned are recorded for reporting.
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
        var audioInputBytes = GetLength(audioSpeechStream);
        var stopwatch = Stopwatch.StartNew();
        var response = await base.GetTextAsync(audioSpeechStream, AIUsageLabels.ForProvider(options), cancellationToken);
        stopwatch.Stop();

        if (response is not null)
        {
            var segmentCount = response.Contents.OfType<TextContent>().Count(content => !string.IsNullOrEmpty(content.Text));

            await RecordAsync(options, response.ModelId, response.ResponseId, response.Usage, response.StartTime, response.EndTime, audioInputBytes, segmentCount, stopwatch.Elapsed.TotalMilliseconds, false, cancellationToken);
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
        var audioInputBytes = GetLength(audioSpeechStream);
        var stopwatch = Stopwatch.StartNew();
        var usage = new AIStreamingUsageAccumulator();
        TimeSpan? startTime = null;
        TimeSpan? endTime = null;
        var segmentCount = 0;

        await foreach (var update in base.GetStreamingTextAsync(audioSpeechStream, AIUsageLabels.ForProvider(options), cancellationToken))
        {
            usage.Add(update.ModelId, update.ResponseId, update.Contents);

            if (update.StartTime is { } updateStartTime && (startTime is null || updateStartTime < startTime))
            {
                startTime = updateStartTime;
            }

            if (update.EndTime is { } updateEndTime && (endTime is null || updateEndTime > endTime))
            {
                endTime = updateEndTime;
            }

            if (update.Contents.OfType<TextContent>().Any(content => !string.IsNullOrEmpty(content.Text)))
            {
                segmentCount++;
            }

            yield return update;
        }

        stopwatch.Stop();

        if (usage.HasUpdates)
        {
            await RecordAsync(options, usage.ModelId, usage.ResponseId, usage.Usage, startTime, endTime, audioInputBytes, segmentCount, stopwatch.Elapsed.TotalMilliseconds, true, cancellationToken);
        }
    }

    private static long GetLength(Stream stream)
    {
        try
        {
            return stream is { CanSeek: true } ? Math.Max(0, stream.Length - stream.Position) : 0;
        }
        catch (Exception ex) when (ex is NotSupportedException or ObjectDisposedException)
        {
            return 0;
        }
    }

    private Task RecordAsync(
        SpeechToTextOptions options,
        string modelId,
        string responseId,
        UsageDetails usage,
        TimeSpan? startTime,
        TimeSpan? endTime,
        long audioInputBytes,
        int segmentCount,
        double responseLatencyMs,
        bool isStreaming,
        CancellationToken cancellationToken)
    {
        var audioDurationMs = endTime is { } duration && duration > TimeSpan.Zero
            ? (long)duration.TotalMilliseconds
            : 0;
        var audioStartMs = startTime is { } start && start > TimeSpan.Zero
            ? (long)start.TotalMilliseconds
            : 0;

        return _recorder.RecordAsync(
            AIUsageOperationTypes.SpeechToText,
            options?.AdditionalProperties,
            modelId,
            responseId,
            usage,
            responseLatencyMs,
            isStreaming,
            record =>
            {
                record.AudioDurationMs = record.AudioDurationMs > 0 ? record.AudioDurationMs : audioDurationMs;
                record.AudioStartMs = audioStartMs;
                record.AudioInputBytes = audioInputBytes;
                record.SegmentCount = segmentCount;
                record.AudioSampleRate = options?.SpeechSampleRate ?? 0;
                record.Language = options?.SpeechLanguage;
                record.TargetLanguage = options?.TextLanguage;
            },
            cancellationToken);
    }
}
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
