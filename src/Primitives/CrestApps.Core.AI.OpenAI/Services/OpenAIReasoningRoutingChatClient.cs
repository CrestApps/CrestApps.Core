using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.OpenAI.Services;

/// <summary>
/// Routes requests that combine function tools with a reasoning effort to the Responses API, and all others to
/// Chat Completions. Reasoning models reject that combination on Chat Completions.
/// </summary>
public sealed class OpenAIReasoningRoutingChatClient : IChatClient
{
    private readonly IChatClient _chatCompletionsClient;
    private readonly IChatClient _responsesClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIReasoningRoutingChatClient"/> class.
    /// </summary>
    /// <param name="chatCompletionsClient">The Chat Completions client.</param>
    /// <param name="responsesClient">The Responses client.</param>
    public OpenAIReasoningRoutingChatClient(IChatClient chatCompletionsClient, IChatClient responsesClient)
    {
        ArgumentNullException.ThrowIfNull(chatCompletionsClient);
        ArgumentNullException.ThrowIfNull(responsesClient);

        _chatCompletionsClient = chatCompletionsClient;
        _responsesClient = responsesClient;
    }

    /// <inheritdoc/>
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        => SelectClient(options).GetResponseAsync(messages, options, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        => SelectClient(options).GetStreamingResponseAsync(messages, options, cancellationToken);

    /// <inheritdoc/>
    public object GetService(Type serviceType, object serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is null && serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return _chatCompletionsClient.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _chatCompletionsClient.Dispose();
        _responsesClient.Dispose();
    }

    internal static bool RequiresResponsesApi(ChatOptions options)
        => options?.Tools is { Count: > 0 } &&
            options.Reasoning?.Effort is ReasoningEffort effort &&
            effort != ReasoningEffort.None;

    private IChatClient SelectClient(ChatOptions options)
        => RequiresResponsesApi(options) ? _responsesClient : _chatCompletionsClient;
}
