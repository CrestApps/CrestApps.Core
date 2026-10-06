using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // This client type is for evaluation purposes only and may change in future updates.
/// <summary>
/// Labels every transcription sent through a speech-to-text client with a usage category and purpose.
/// </summary>
internal sealed class AIUsageLabelingSpeechToTextClient : DelegatingSpeechToTextClient
{
    private readonly string _contextType;
    private readonly string _purpose;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageLabelingSpeechToTextClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/>.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/>.</param>
    public AIUsageLabelingSpeechToTextClient(
        ISpeechToTextClient innerClient,
        string contextType,
        string purpose)
        : base(innerClient)
    {
        _contextType = contextType;
        _purpose = purpose;
    }

    /// <summary>
    /// Transcribes the audio with the request labeled.
    /// </summary>
    /// <param name="audioSpeechStream">The audio to transcribe.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetTextAsync(audioSpeechStream, Label(options), cancellationToken);
    }

    /// <summary>
    /// Transcribes the audio as a stream of updates with the request labeled.
    /// </summary>
    /// <param name="audioSpeechStream">The audio to transcribe.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingTextAsync(audioSpeechStream, Label(options), cancellationToken);
    }

    private SpeechToTextOptions Label(SpeechToTextOptions options)
    {
        var labeled = options?.Clone() ?? new SpeechToTextOptions();
        labeled.AdditionalProperties = AIUsageLabels.Apply(labeled.AdditionalProperties, _contextType, _purpose);

        return labeled;
    }
}
#pragma warning restore MEAI001 // This client type is for evaluation purposes only and may change in future updates.
