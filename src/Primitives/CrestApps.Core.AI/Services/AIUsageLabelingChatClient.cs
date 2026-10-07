using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Labels every request sent through a chat client with a usage category and purpose.
/// </summary>
internal sealed class AIUsageLabelingChatClient : DelegatingChatClient
{
    private readonly string _contextType;
    private readonly string _purpose;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageLabelingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/>.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/>.</param>
    public AIUsageLabelingChatClient(
        IChatClient innerClient,
        string contextType,
        string purpose)
        : base(innerClient)
    {
        _contextType = contextType;
        _purpose = purpose;
    }

    /// <summary>
    /// Gets a response with the request labeled.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(messages, Label(options), cancellationToken);
    }

    /// <summary>
    /// Gets a streaming response with the request labeled.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(messages, Label(options), cancellationToken);
    }

    private ChatOptions Label(ChatOptions options)
    {
        var labeled = options?.Clone() ?? new ChatOptions();
        labeled.AdditionalProperties = AIUsageLabels.Apply(labeled.AdditionalProperties, _contextType, _purpose);

        return labeled;
    }
}
