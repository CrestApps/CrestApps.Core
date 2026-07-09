#pragma warning disable MEAI001
using Microsoft.Extensions.AI;
using Polly;

namespace CrestApps.Core.AI.Resilience.Services;

/// <summary>
/// Applies a Polly resilience pipeline to speech-to-text requests.
/// </summary>
internal sealed class AIResilienceSpeechToTextClient : ISpeechToTextClient
{
    private readonly ISpeechToTextClient _innerClient;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIResilienceSpeechToTextClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner speech-to-text client.</param>
    /// <param name="pipeline">The resilience pipeline.</param>
    public AIResilienceSpeechToTextClient(
        ISpeechToTextClient innerClient,
        ResiliencePipeline pipeline)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <summary>
    /// Gets a transcription through the configured resilience pipeline.
    /// </summary>
    /// <param name="audioSpeechStream">The audio input stream.</param>
    /// <param name="options">The transcription options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The transcription response.</returns>
    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSpeechStream);

        if (audioSpeechStream.CanSeek)
        {
            var startPosition = audioSpeechStream.Position;

            return await _pipeline.ExecuteAsync(
                static async (state, token) =>
                {
                    state.AudioSpeechStream.Position = state.StartPosition;

                    using var attemptStream = new NonDisposingStream(state.AudioSpeechStream);

                    return await state.InnerClient.GetTextAsync(attemptStream, state.Options, token);
                },
                (InnerClient: _innerClient, AudioSpeechStream: audioSpeechStream, StartPosition: startPosition, Options: options),
                cancellationToken);
        }

        using var copyStream = new MemoryStream();
        await audioSpeechStream.CopyToAsync(copyStream, cancellationToken);

        var bufferedAudio = copyStream.ToArray();

        return await _pipeline.ExecuteAsync(
            static async (state, token) =>
            {
                using var attemptStream = new MemoryStream(state.BufferedAudio, writable: false);

                return await state.InnerClient.GetTextAsync(attemptStream, state.Options, token);
            },
            (InnerClient: _innerClient, BufferedAudio: bufferedAudio, Options: options),
            cancellationToken);
    }

    /// <summary>
    /// Gets a streaming transcription through the configured resilience pipeline when the input stream is replayable.
    /// </summary>
    /// <param name="audioSpeechStream">The audio input stream.</param>
    /// <param name="options">The transcription options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A streaming sequence of transcription updates.</returns>
    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSpeechStream);

        if (!audioSpeechStream.CanSeek)
        {
            await foreach (var update in _innerClient.GetStreamingTextAsync(audioSpeechStream, options, cancellationToken))
            {
                yield return update;
            }

            yield break;
        }

        var startPosition = audioSpeechStream.Position;
        var streamingResult = await _pipeline.ExecuteAsync(
            static async (state, token) =>
            {
                state.AudioSpeechStream.Position = state.StartPosition;
                var attemptStream = new NonDisposingStream(state.AudioSpeechStream);
                var enumerator = state.InnerClient.GetStreamingTextAsync(attemptStream, state.Options, token)
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
                    attemptStream.Dispose();
                    throw;
                }
            },
            (InnerClient: _innerClient, AudioSpeechStream: audioSpeechStream, StartPosition: startPosition, Options: options),
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
    /// Gets a service exposed by the inner speech-to-text client.
    /// </summary>
    /// <param name="serviceType">The service type.</param>
    /// <param name="serviceKey">The optional service key.</param>
    /// <returns>The resolved service, if any.</returns>
    public object GetService(Type serviceType, object serviceKey)
    {
        return _innerClient.GetService(serviceType, serviceKey);
    }

    /// <summary>
    /// Disposes the inner speech-to-text client.
    /// </summary>
    public void Dispose()
    {
        _innerClient.Dispose();
    }

    private readonly record struct StreamingResult(
        IAsyncEnumerator<SpeechToTextResponseUpdate> Enumerator,
        bool HasFirstUpdate,
        SpeechToTextResponseUpdate FirstUpdate);
}
#pragma warning restore MEAI001
