#pragma warning disable MEAI001
using Microsoft.Extensions.AI;
using Polly;

namespace CrestApps.Core.AI.Resilience.Services;

/// <summary>
/// Applies a Polly resilience pipeline to text-to-speech requests.
/// </summary>
internal sealed class AIResilienceTextToSpeechClient : ITextToSpeechClient
{
    private readonly ITextToSpeechClient _innerClient;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIResilienceTextToSpeechClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner text-to-speech client.</param>
    /// <param name="pipeline">The resilience pipeline.</param>
    public AIResilienceTextToSpeechClient(
        ITextToSpeechClient innerClient,
        ResiliencePipeline pipeline)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <summary>
    /// Gets generated audio through the configured resilience pipeline.
    /// </summary>
    /// <param name="text">The text to synthesize.</param>
    /// <param name="options">The synthesis options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generated audio response.</returns>
    public Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        return _pipeline.ExecuteAsync(
            static async (state, token) => await state.InnerClient.GetAudioAsync(state.Text, state.Options, token),
            (InnerClient: _innerClient, Text: text, Options: options),
            cancellationToken)
            .AsTask();
    }

    /// <summary>
    /// Gets a streaming audio response through the configured resilience pipeline.
    /// </summary>
    /// <param name="text">The text to synthesize.</param>
    /// <param name="options">The synthesis options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A streaming sequence of audio updates.</returns>
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var streamingResult = await _pipeline.ExecuteAsync(
            static async (state, token) =>
            {
                var enumerator = state.InnerClient.GetStreamingAudioAsync(state.Text, state.Options, token)
                    .GetAsyncEnumerator(token);

                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        return new StreamingResult(enumerator, HasFirstUpdate: false, FirstUpdate: null);
                    }

                    return new StreamingResult(enumerator, HasFirstUpdate: true, FirstUpdate: enumerator.Current);
                }
                catch
                {
                    await enumerator.DisposeAsync();
                    throw;
                }
            },
            (InnerClient: _innerClient, Text: text, Options: options),
            cancellationToken);

        await using var enumerator = streamingResult.Enumerator;

        if (streamingResult.HasFirstUpdate)
        {
            yield return streamingResult.FirstUpdate!;
        }

        while (await enumerator.MoveNextAsync())
        {
            yield return enumerator.Current;
        }
    }

    /// <summary>
    /// Gets a service exposed by the inner text-to-speech client.
    /// </summary>
    /// <param name="serviceType">The service type.</param>
    /// <param name="serviceKey">The optional service key.</param>
    /// <returns>The resolved service, if any.</returns>
    public object GetService(Type serviceType, object serviceKey)
    {
        return _innerClient.GetService(serviceType, serviceKey);
    }

    /// <summary>
    /// Disposes the inner text-to-speech client.
    /// </summary>
    public void Dispose()
    {
        _innerClient.Dispose();
    }

    private readonly record struct StreamingResult(
        IAsyncEnumerator<TextToSpeechResponseUpdate> Enumerator,
        bool HasFirstUpdate,
        TextToSpeechResponseUpdate FirstUpdate);
}
#pragma warning restore MEAI001
