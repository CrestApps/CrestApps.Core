using Microsoft.Extensions.AI;
using OpenAI.Responses;

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
        => RequiresResponsesApi(options)
            ? _responsesClient.GetResponseAsync(messages, WithoutStoredOutput(options), cancellationToken)
            : _chatCompletionsClient.GetResponseAsync(messages, options, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        => RequiresResponsesApi(options)
            ? _responsesClient.GetStreamingResponseAsync(messages, WithoutStoredOutput(options), cancellationToken)
            : _chatCompletionsClient.GetStreamingResponseAsync(messages, options, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Services, such as <see cref="ChatClientMetadata"/>, come from the Chat Completions client even for requests
    /// that are sent to the Responses API.
    /// </remarks>
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

#pragma warning disable OPENAI001 // The Responses types from the OpenAI SDK are for evaluation purposes only.
    /// <summary>
    /// Keeps the conversation off OpenAI's servers. Without a stored response the model's reasoning cannot be looked
    /// up between tool-call turns, so it is returned encrypted and sent back with the next turn instead.
    /// </summary>
    /// <param name="options">The request options.</param>
    internal static ChatOptions WithoutStoredOutput(ChatOptions options)
    {
        var responsesOptions = options.Clone();
        var rawRepresentationFactory = options.RawRepresentationFactory;

        responsesOptions.RawRepresentationFactory = client =>
        {
            var createOptions = rawRepresentationFactory?.Invoke(client) as CreateResponseOptions ?? new CreateResponseOptions();

            createOptions.StoredOutputEnabled = false;

            if (!createOptions.IncludedProperties.Contains(IncludedResponseProperty.ReasoningEncryptedContent))
            {
                createOptions.IncludedProperties.Add(IncludedResponseProperty.ReasoningEncryptedContent);
            }

            return createOptions;
        };

        return responsesOptions;
    }
#pragma warning restore OPENAI001
}
