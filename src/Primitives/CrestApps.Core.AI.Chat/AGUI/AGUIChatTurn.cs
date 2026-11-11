using System.Text.Json.Nodes;
using System.Threading.Channels;
using CrestApps.Core.AI.AGUI;
using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.AGUI;

/// <summary>
/// One AG-UI run of the AI chat. It is passed down the chat hub's message pipeline in place of the
/// <see cref="CompletionPartialMessage"/> channel, so every processor keeps its signature: the chat processor writes
/// the full AG-UI events through <see cref="Events"/>, and any other processor's partial messages become AG-UI text.
/// </summary>
public sealed class AGUIChatTurn : ChannelWriter<CompletionPartialMessage>
{
    /// <summary>
    /// The name of the application event that carries an assistant message's title and appearance.
    /// </summary>
    public const string AssistantMessageEventName = "crestapps.assistant_message";

    /// <summary>
    /// The name of the application event that carries an assistant message's references (citations and files).
    /// </summary>
    public const string ReferencesEventName = "crestapps.references";

    /// <summary>
    /// The name of the application event that replaces the text of an assistant message, for example when the output
    /// checks blocked what streamed.
    /// </summary>
    public const string MessageReplacedEventName = "crestapps.message_replaced";

    /// <summary>
    /// Initializes a new instance of the <see cref="AGUIChatTurn"/> class.
    /// </summary>
    /// <param name="request">What the run input asks for.</param>
    /// <param name="events">The writer of the run's events.</param>
    public AGUIChatTurn(AGUIRunRequest request, AGUIRunEventWriter events)
    {
        Request = request;
        Events = events;
    }

    /// <summary>
    /// Gets what the run input asks for.
    /// </summary>
    public AGUIRunRequest Request { get; }

    /// <summary>
    /// Gets the writer of the run's events.
    /// </summary>
    public AGUIRunEventWriter Events { get; }

    /// <summary>
    /// Gets or sets the state the client shares with the agent in this run, once the chat set it up.
    /// </summary>
    public ChatSharedState SharedState { get; set; }

    /// <summary>
    /// Writes a partial message of a processor that does not write AG-UI events itself, as AG-UI text. A message
    /// whose text streamed already is replaced instead.
    /// </summary>
    /// <param name="item">The partial message.</param>
    /// <returns><see langword="true"/>.</returns>
    public override bool TryWrite(CompletionPartialMessage item)
    {
        WriteAsync(item).AsTask().GetAwaiter().GetResult();

        return true;
    }

    /// <summary>
    /// Writes a partial message of a processor that does not write AG-UI events itself, as AG-UI text. A message
    /// whose text streamed already is replaced instead.
    /// </summary>
    /// <param name="item">The partial message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the events are written.</returns>
    public override async ValueTask WriteAsync(CompletionPartialMessage item, CancellationToken cancellationToken = default)
    {
        if (item is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(item.SessionId) && string.IsNullOrEmpty(Events.ThreadId) && !Events.IsStarted)
        {
            Events.ThreadId = item.SessionId;
        }

        var messageId = item.MessageId ?? Events.RunId;

        if (Events.HasText(messageId))
        {
            await Events.WriteCustomAsync(MessageReplacedEventName, new Dictionary<string, object>
            {
                ["messageId"] = messageId,
                ["content"] = item.Content ?? string.Empty,
            }, CancellationToken.None);

            return;
        }

        if (item.References is { Count: > 0 })
        {
            await WriteReferencesAsync(messageId, item.References, CancellationToken.None);
        }

        await Events.WriteTextAsync(messageId, item.Content, CancellationToken.None);
    }

    /// <summary>
    /// Tells the channel that no more partial messages come. The run itself is ended by the hub.
    /// </summary>
    /// <param name="error">The error that ended the pipeline, if any.</param>
    /// <returns><see langword="true"/>.</returns>
    public override bool TryComplete(Exception error = null)
    {
        return true;
    }

    /// <summary>
    /// Always lets the processor write.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/>.</returns>
    public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(true);
    }

    /// <summary>
    /// Writes the title and appearance of an assistant message, before its text.
    /// </summary>
    /// <param name="messageId">The id of the assistant message.</param>
    /// <param name="title">The title, or <see langword="null"/>.</param>
    /// <param name="appearance">The appearance, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public ValueTask WriteAssistantMessageAsync(string messageId, string title, AssistantMessageAppearance appearance, CancellationToken cancellationToken = default)
    {
        return Events.WriteCustomAsync(AssistantMessageEventName, new Dictionary<string, object>
        {
            ["messageId"] = messageId,
            ["title"] = title,
            ["appearance"] = appearance,
        }, cancellationToken);
    }

    /// <summary>
    /// Writes the references of an assistant message.
    /// </summary>
    /// <param name="messageId">The id of the assistant message.</param>
    /// <param name="references">The references, keyed by marker.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public ValueTask WriteReferencesAsync(string messageId, IReadOnlyDictionary<string, AICompletionReference> references, CancellationToken cancellationToken = default)
    {
        return Events.WriteCustomAsync(ReferencesEventName, new Dictionary<string, object>
        {
            ["messageId"] = messageId,
            ["references"] = references,
        }, cancellationToken);
    }

    /// <summary>
    /// Sets up the state the client shares with the agent: writes it back as a snapshot, and writes each change the
    /// agent makes as a delta.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The shared state, or <see langword="null"/> when the client shares none.</returns>
    public async ValueTask<ChatSharedState> InitializeSharedStateAsync(CancellationToken cancellationToken = default)
    {
        if (Request.State is null)
        {
            return null;
        }

        SharedState = new ChatSharedState
        {
            Value = (JsonObject)Request.State.DeepClone(),
            OnChangedAsync = (operations, _, token) => Events.WriteStateDeltaAsync(operations, token),
        };

        await Events.WriteStateSnapshotAsync(SharedState.Value, cancellationToken);

        return SharedState;
    }
}
