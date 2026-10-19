using CrestApps.Core.AI.Speech;
using Microsoft.Extensions.AI;
using OpenAI.Audio;

namespace CrestApps.Core.AI.OpenAI.Services;

#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
/// <summary>
/// Gives OpenAI's speech endpoint how to speak: the speaking style as the model's <c>instructions</c>, and the speed
/// within the range OpenAI accepts.
/// </summary>
/// <remarks>
/// <see cref="TextToSpeechOptions"/> has no field for instructions, so they travel as
/// <see cref="TextToSpeechOptionsProperties.Instructions"/> and are handed to the SDK's own options. The first models,
/// <c>tts-1</c> and <c>tts-1-hd</c>, take no instructions, so those models speak without them.
/// </remarks>
internal sealed class OpenAITextToSpeechStyleClient : DelegatingTextToSpeechClient
{
    /// <summary>
    /// The slowest speed OpenAI accepts.
    /// </summary>
    internal const float MinimumSpeed = 0.25f;

    /// <summary>
    /// The fastest speed OpenAI accepts.
    /// </summary>
    internal const float MaximumSpeed = 4f;

    private readonly string _modelId;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAITextToSpeechStyleClient"/> class.
    /// </summary>
    /// <param name="innerClient">The OpenAI text-to-speech client.</param>
    /// <param name="modelId">The model the client speaks with.</param>
    public OpenAITextToSpeechStyleClient(ITextToSpeechClient innerClient, string modelId)
        : base(innerClient)
    {
        _modelId = modelId;
    }

    /// <summary>
    /// Synthesizes the text in the style and at the speed asked for.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetAudioAsync(text, Styled(options), cancellationToken);
    }

    /// <summary>
    /// Synthesizes the text as a stream of audio updates, in the style and at the speed asked for.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingAudioAsync(text, Styled(options), cancellationToken);
    }

    /// <summary>
    /// Determines whether a model takes instructions on how to speak.
    /// </summary>
    /// <param name="modelId">The model.</param>
    internal static bool TakesInstructions(string modelId)
        => string.IsNullOrEmpty(modelId) || !modelId.StartsWith("tts-1", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The options with the instructions handed to the SDK's options and the speed within OpenAI's range.
    /// </summary>
    /// <param name="options">The options asked for.</param>
    private TextToSpeechOptions Styled(TextToSpeechOptions options)
    {
        if (options is null)
        {
            return null;
        }

        var instructions = options.AdditionalProperties?.TryGetValue(TextToSpeechOptionsProperties.Instructions, out var value) == true
            ? (value as string)?.Trim()
            : null;
        var speed = options.Speed is float requested ? Math.Clamp(requested, MinimumSpeed, MaximumSpeed) : (float?)null;

        if (string.IsNullOrEmpty(instructions) && speed == options.Speed)
        {
            return options;
        }

        var styled = options.Clone();
        styled.Speed = speed;

        if (!string.IsNullOrEmpty(instructions) && TakesInstructions(_modelId))
        {
            var inner = options.RawRepresentationFactory;

            styled.RawRepresentationFactory = client =>
            {
                var speechOptions = inner?.Invoke(client) as SpeechGenerationOptions ?? new SpeechGenerationOptions();

#pragma warning disable OPENAI001 // SpeechGenerationOptions.Instructions is for evaluation purposes only.
                speechOptions.Instructions ??= instructions;
#pragma warning restore OPENAI001

                return speechOptions;
            };
        }

        return styled;
    }
}
#pragma warning restore MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
