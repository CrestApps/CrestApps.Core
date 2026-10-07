using System.Diagnostics;
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Meters every chat request made through a chat client the AI client factory created, whatever the provider.
/// </summary>
internal sealed class AICompletionUsageTrackingChatClient : DelegatingChatClient
{
    private readonly AIUsageRecorder _recorder;

    /// <summary>
    /// Initializes a new instance of the <see cref="AICompletionUsageTrackingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client.</param>
    /// <param name="recorder">The recorder that stores the usage.</param>
    public AICompletionUsageTrackingChatClient(
        IChatClient innerClient,
        AIUsageRecorder recorder)
        : base(innerClient)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Gets response.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await base.GetResponseAsync(messages, AIUsageLabels.ForProvider(options), cancellationToken);
        stopwatch.Stop();

        await RecordUsageAsync(response, options, stopwatch.Elapsed.TotalMilliseconds, false, cancellationToken);

        return response;
    }

    /// <summary>
    /// Gets streaming response.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in base.GetStreamingResponseAsync(messages, AIUsageLabels.ForProvider(options), cancellationToken))
        {
            updates.Add(update);
            yield return update;
        }

        stopwatch.Stop();

        if (updates.Count > 0)
        {
            await RecordUsageAsync(updates.ToChatResponse(), options, stopwatch.Elapsed.TotalMilliseconds, true, cancellationToken);
        }
    }

    private Task RecordUsageAsync(
        ChatResponse response,
        ChatOptions options,
        double responseLatencyMs,
        bool isStreaming,
        CancellationToken cancellationToken)
    {
        if (response is null)
        {
            return Task.CompletedTask;
        }

        return _recorder.RecordAsync(
            AIUsageOperationTypes.Chat,
            options?.AdditionalProperties,
            response.ModelId,
            response.ResponseId,
            response.Usage,
            responseLatencyMs,
            isStreaming,
            null,
            cancellationToken);
    }
}
