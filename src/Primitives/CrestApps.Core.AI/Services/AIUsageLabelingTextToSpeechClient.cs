using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // This client type is for evaluation purposes only and may change in future updates.
/// <summary>
/// Labels every synthesis sent through a text-to-speech client with a usage category and purpose.
/// </summary>
internal sealed class AIUsageLabelingTextToSpeechClient : DelegatingTextToSpeechClient
{
    private readonly string _contextType;
    private readonly string _purpose;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageLabelingTextToSpeechClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/>.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/>.</param>
    public AIUsageLabelingTextToSpeechClient(
        ITextToSpeechClient innerClient,
        string contextType,
        string purpose)
        : base(innerClient)
    {
        _contextType = contextType;
        _purpose = purpose;
    }

    /// <summary>
    /// Synthesizes the text with the request labeled.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetAudioAsync(text, Label(options), cancellationToken);
    }

    /// <summary>
    /// Synthesizes the text as a stream of audio updates with the request labeled.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingAudioAsync(text, Label(options), cancellationToken);
    }

    private TextToSpeechOptions Label(TextToSpeechOptions options)
    {
        var labeled = options?.Clone() ?? new TextToSpeechOptions();
        labeled.AdditionalProperties = AIUsageLabels.Apply(labeled.AdditionalProperties, _contextType, _purpose);

        return labeled;
    }
}
#pragma warning restore MEAI001 // This client type is for evaluation purposes only and may change in future updates.
