using Microsoft.Extensions.AI;
using Polly;

namespace CrestApps.Core.AI.Resilience.Services;

/// <summary>
/// Applies a Polly resilience pipeline to chat-client requests.
/// </summary>
internal sealed class AIResilienceChatClient : DelegatingChatClient
{
    private readonly IChatClient _innerClient;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIResilienceChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner chat client.</param>
    /// <param name="pipeline">The resilience pipeline.</param>
    public AIResilienceChatClient(
        IChatClient innerClient,
        ResiliencePipeline pipeline)
        : base(innerClient)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <summary>
    /// Gets a chat response through the configured resilience pipeline.
    /// </summary>
    /// <param name="messages">The chat messages.</param>
    /// <param name="options">The chat options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The chat response.</returns>
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var request = new ChatRequest(messages as IReadOnlyList<ChatMessage> ?? messages.ToList(), options);

        return _pipeline.ExecuteAsync(
            static async (state, token) => await state.InnerClient.GetResponseAsync(state.Messages, state.Options, token),
            (InnerClient: _innerClient, request.Messages, request.Options),
            cancellationToken)
            .AsTask();
    }

    /// <summary>
    /// Gets a streaming chat response through the configured resilience pipeline.
    /// </summary>
    /// <param name="messages">The chat messages.</param>
    /// <param name="options">The chat options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A streaming sequence of response updates.</returns>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var request = new ChatRequest(messages as IReadOnlyList<ChatMessage> ?? messages.ToList(), options);

        var streamingResult = await _pipeline.ExecuteAsync(
            static async (state, token) =>
            {
                var enumerator = state.InnerClient.GetStreamingResponseAsync(state.Messages, state.Options, token)
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
            (InnerClient: _innerClient, Messages: request.Messages, Options: request.Options),
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

    private readonly record struct ChatRequest(
        IReadOnlyList<ChatMessage> Messages,
        ChatOptions Options);

    private readonly record struct StreamingResult(
        IAsyncEnumerator<ChatResponseUpdate> Enumerator,
        bool HasFirstUpdate,
        ChatResponseUpdate FirstUpdate);
}
