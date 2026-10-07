using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Intelligence;

/// <summary>
/// A chat client that answers with canned text and records what it was asked, so the model paths of the PDF
/// tools can be tested without a network.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Func<FakeChatRequest, string> _respond;
    private readonly Lock _lock = new();
    private readonly List<FakeChatRequest> _requests = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeChatClient"/> class.
    /// </summary>
    /// <param name="respond">Writes the answer to a request.</param>
    public FakeChatClient(Func<FakeChatRequest, string> respond)
    {
        _respond = respond;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeChatClient"/> class that always gives the same answer.
    /// </summary>
    /// <param name="answer">The answer.</param>
    public FakeChatClient(string answer)
        : this(_ => answer)
    {
    }

    /// <summary>
    /// Gets a snapshot of the requests made so far.
    /// </summary>
    public IReadOnlyList<FakeChatRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    /// Answers a request.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The canned answer.</returns>
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var request = new FakeChatRequest(
            string.Join("\n", list.Where(message => message.Role == ChatRole.System).Select(message => message.Text)),
            string.Join("\n", list.Where(message => message.Role == ChatRole.User).Select(message => message.Text)),
            list.SelectMany(message => message.Contents).OfType<DataContent>().ToList(),
            options);

        lock (_lock)
        {
            _requests.Add(request);
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _respond(request))));
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Never returns.</returns>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Returns no services.
    /// </summary>
    /// <param name="serviceType">The service type.</param>
    /// <param name="serviceKey">The service key.</param>
    /// <returns><see langword="null"/>.</returns>
    public object GetService(Type serviceType, object serviceKey = null)
    {
        return null;
    }

    /// <summary>
    /// Releases nothing.
    /// </summary>
    public void Dispose()
    {
    }
}

/// <summary>
/// One request a <see cref="FakeChatClient"/> received.
/// </summary>
/// <param name="System">The system instructions.</param>
/// <param name="User">The user text.</param>
/// <param name="Images">The pictures sent.</param>
/// <param name="Options">The options.</param>
internal sealed record FakeChatRequest(string System, string User, IReadOnlyList<DataContent> Images, ChatOptions Options);
