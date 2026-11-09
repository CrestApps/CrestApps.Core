using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Telemetry;

/// <summary>
/// Asks for a tool on the first call and answers on the second, like a model that used the tool.
/// </summary>
internal sealed class ToolCallingChatClient : IChatClient
{
    private readonly string _toolName;
    private int _calls;

    public ToolCallingChatClient(string toolName)
    {
        _toolName = toolName;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
    {
        var message = Interlocked.Increment(ref _calls) == 1
            ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", _toolName)])
            : new ChatMessage(ChatRole.Assistant, "Done.");

        return Task.FromResult(new ChatResponse(message));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public object GetService(Type serviceType, object serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}
