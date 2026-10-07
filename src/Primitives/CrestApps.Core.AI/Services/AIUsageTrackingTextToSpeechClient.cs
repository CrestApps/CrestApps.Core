using System.Diagnostics;
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
/// <summary>
/// Meters every synthesis made through a text-to-speech client the AI client factory created, whatever the
/// provider. Both the characters synthesized and any tokens reported are recorded, because models bill one or the
/// other.
/// </summary>
internal sealed class AIUsageTrackingTextToSpeechClient : DelegatingTextToSpeechClient
{
    private readonly AIUsageRecorder _recorder;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageTrackingTextToSpeechClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client.</param>
    /// <param name="recorder">The recorder that stores the usage.</param>
    public AIUsageTrackingTextToSpeechClient(
        ITextToSpeechClient innerClient,
        AIUsageRecorder recorder)
        : base(innerClient)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Synthesizes the text and records its usage.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await base.GetAudioAsync(text, AIUsageLabels.ForProvider(options), cancellationToken);
        stopwatch.Stop();

        if (response is not null)
        {
            await RecordAsync(text, options, response.ModelId, response.ResponseId, response.Usage, stopwatch.Elapsed.TotalMilliseconds, false, cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Synthesizes the text as a stream of audio updates and records its usage once the stream ends.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var usage = new AIStreamingUsageAccumulator();

        await foreach (var update in base.GetStreamingAudioAsync(text, AIUsageLabels.ForProvider(options), cancellationToken))
        {
            usage.Add(update.ModelId, update.ResponseId, update.Contents);

            yield return update;
        }

        stopwatch.Stop();

        if (usage.HasUpdates)
        {
            await RecordAsync(text, options, usage.ModelId, usage.ResponseId, usage.Usage, stopwatch.Elapsed.TotalMilliseconds, true, cancellationToken);
        }
    }

    private Task RecordAsync(
        string text,
        TextToSpeechOptions options,
        string modelId,
        string responseId,
        UsageDetails usage,
        double responseLatencyMs,
        bool isStreaming,
        CancellationToken cancellationToken)
    {
        var characterCount = text?.Length ?? 0;

        return _recorder.RecordAsync(
            AIUsageOperationTypes.TextToSpeech,
            options?.AdditionalProperties,
            modelId,
            responseId,
            usage,
            responseLatencyMs,
            isStreaming,
            record => record.CharacterCount = record.CharacterCount > 0 ? record.CharacterCount : characterCount,
            cancellationToken);
    }
}
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
