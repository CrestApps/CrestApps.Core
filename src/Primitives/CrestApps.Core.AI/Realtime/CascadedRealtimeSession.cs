#pragma warning disable MEAI001 // The realtime types from Microsoft.Extensions.AI are for evaluation purposes only.
#nullable enable
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// A realtime session that answers speech with speech by chaining three ordinary clients: a realtime
/// transcription session for the user's audio, an <see cref="IChatClient"/> for the reply, and an
/// <see cref="ITextToSpeechClient"/> to speak it.
/// </summary>
/// <remarks>
/// <para>
/// The session presents itself to callers exactly like a native speech-to-speech provider: it emits the
/// same <see cref="RealtimeServerMessage"/> types, so <c>DefaultRealtimeConversation</c> and everything
/// above it work unchanged.
/// </para>
/// <para>
/// Messages are produced by two writers — the transcription pump and the current assistant turn — and
/// read by a single caller, so they meet in an unbounded channel rather than being yielded directly.
/// That also means messages produced before the caller starts enumerating are kept rather than lost.
/// </para>
/// <para>
/// Everything that changes the conversation -- what the user said, knowledge the host adds, a reply to give -- is
/// queued as work for a single consumer. That keeps the history ordered, lets work be queued without waiting for the
/// reply in progress, and means only one reply is ever being written and spoken.
/// </para>
/// <para>
/// Whether a finished utterance is answered on its own follows the session's turn detection, as it does for a
/// native provider: a session that does not respond automatically waits for the host to ask, after adding whatever
/// knowledge the reply should draw on.
/// </para>
/// </remarks>
internal sealed class CascadedRealtimeSession : IRealtimeClientSession
{
    // A reply is spoken a sentence at a time so the first audio starts playing while the model is still
    // writing. Below this length a fragment is held back and joined to the next one: sending two or three
    // words to a speech model produces clipped, oddly-cadenced audio.
    private const int MinimumSpeakableLength = 24;

    // Assistant audio is emitted straight onto the realtime timeline, where it is played as raw PCM
    // frames. A speech model that answers with a container instead — MP3, WAV — would be played as noise,
    // so the format is checked rather than trusted.
    private const string PcmMediaType = "audio/L16";

    private readonly IRealtimeClientSession _transcription;
    private readonly IChatClient _chatClient;
    private readonly ITextToSpeechClient _speechClient;
    private readonly ChatOptions? _chatOptions;
    private readonly TextToSpeechOptions _speechOptions;
    private readonly string? _instructions;
    private readonly bool _respondsAutomatically;

    // The conversation so far, without the session's instructions. Only the work consumer reads or changes it.
    private readonly List<ChatMessage> _history = [];

    /// <summary>
    /// What a requested reply that follows the assistant's own line is answering: nothing new from the user.
    /// </summary>
    internal const string NothingNewFromTheUser = "(The user has not said anything new. Reply as your instructions for this reply say.)";

    private readonly Channel<QueuedWork> _work = Channel.CreateUnbounded<QueuedWork>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    // The cancellation of every reply that is queued or speaking, so that talking over the assistant can stop all
    // of them, not just the one that happens to be speaking.
    private readonly HashSet<CancellationTokenSource> _replies = [];
    private readonly Channel<RealtimeServerMessage> _messages = Channel.CreateUnbounded<RealtimeServerMessage>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly CancellationTokenSource _sessionCancellation = new();
    private readonly ILogger _logger;

    private Task? _pump;
    private Task? _consumer;
    private int _disposed;

    /// <inheritdoc />
    public RealtimeSessionOptions? Options { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CascadedRealtimeSession"/> class.
    /// </summary>
    /// <param name="transcription">The realtime transcription session that hears the user.</param>
    /// <param name="chatClient">The chat client that writes the reply.</param>
    /// <param name="speechClient">The text-to-speech client that speaks the reply.</param>
    /// <param name="options">The session options the caller requested.</param>
    /// <param name="chatOptions">The chat options to apply to every turn, or <see langword="null"/> for none.</param>
    /// <param name="speechOptions">The speech options to apply to every spoken fragment.</param>
    /// <param name="logger">The logger.</param>
    public CascadedRealtimeSession(
        IRealtimeClientSession transcription,
        IChatClient chatClient,
        ITextToSpeechClient speechClient,
        RealtimeSessionOptions? options,
        ChatOptions? chatOptions,
        TextToSpeechOptions speechOptions,
        ILogger logger)
    {
        _transcription = transcription;
        _chatClient = chatClient;
        _speechClient = speechClient;
        Options = options;
        _chatOptions = chatOptions;
        _speechOptions = speechOptions;
        _logger = logger;

        _instructions = string.IsNullOrWhiteSpace(options?.Instructions) ? null : options.Instructions;

        // The same signal a native provider is given: a host that grounds each turn turns automatic replies off and
        // asks for the reply itself once the knowledge is in.
        _respondsAutomatically = options?.RawRepresentationFactory?.Invoke() is not RealtimeTurnDetectionOverrides { CreateResponse: false };
    }

    /// <summary>
    /// Starts relaying the transcription session. Called once, by the client that created this session.
    /// </summary>
    public void Start()
    {
        _consumer ??= Task.Run(() => ConsumeWorkAsync(_sessionCancellation.Token));
        _pump ??= Task.Run(() => PumpTranscriptionAsync(_sessionCancellation.Token));
    }

    /// <inheritdoc />
    public async Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        switch (message)
        {
            // A reply nobody spoke to ask for (the opening line of a call, a prompt after a silence), or the reply a
            // host asks for once it has added knowledge to the conversation, is the cascade's own to give. The
            // transcription leg only hears audio: handed this, it had no reply to write.
            case CreateResponseRealtimeClientMessage response:
                Enqueue(new QueuedWork(Message: null, Respond: true, response, NewReplyCancellation()));

                return;

            // Knowledge the host adds goes where the reply is written from, the chat leg's history.
            case CreateConversationItemRealtimeClientMessage { Item: { } item }:
                if (ToChatMessage(item) is { } chatMessage)
                {
                    Enqueue(new QueuedWork(chatMessage, Respond: false, Response: null, Cancellation: null));
                }

                return;
        }

        // Everything else the caller sends is about the user's audio, which only the transcription leg can act
        // on. It decides for itself what it cannot honor.
        await _transcription.SendAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _messages.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return _transcription.GetService(serviceType)
            ?? _chatClient.GetService(serviceType)
            ?? _speechClient.GetService(serviceType);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _sessionCancellation.CancelAsync();

        _work.Writer.TryComplete();

        // The pump and the consumer own the inner clients while they run, so let them unwind before disposing.
        await SafeAwaitAsync(_pump);
        await SafeAwaitAsync(_consumer);

        await _transcription.DisposeAsync();

        _chatClient.Dispose();
        _speechClient.Dispose();
        _sessionCancellation.Dispose();
    }

    /// <summary>
    /// Relays the transcription session, starting an assistant turn each time the user finishes speaking.
    /// </summary>
    /// <param name="cancellationToken">A token that ends the session.</param>
    private async Task PumpTranscriptionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _transcription.GetStreamingResponseAsync(cancellationToken))
            {
                if (message.Type == RealtimeServerMessageType.InputAudioTranscriptionDelta)
                {
                    // The transcription leg reports no speech-start event of its own, but a partial
                    // transcript only exists because the user is talking. Treating the first one as the
                    // start of a turn is what lets a caller cut off audio that is still playing.
                    await BargeInAsync(message);
                }

                await _messages.Writer.WriteAsync(message, cancellationToken);

                if (message.Type == RealtimeServerMessageType.InputAudioTranscriptionCompleted &&
                    message is InputAudioTranscriptionRealtimeServerMessage transcription &&
                    !string.IsNullOrWhiteSpace(transcription.Transcription))
                {
                    // What the user said always joins the conversation. Answering it is left to the host when the
                    // session does not respond on its own: it has knowledge to add first, and then asks.
                    Enqueue(new QueuedWork(
                        new ChatMessage(ChatRole.User, transcription.Transcription),
                        _respondsAutomatically,
                        Response: null,
                        _respondsAutomatically ? NewReplyCancellation() : null));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The session was disposed.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The cascaded realtime transcription relay failed.");

            await WriteErrorAsync(exception);
        }
        finally
        {
            // The reply to the last thing the user said is still being written and spoken by the consumer.
            // Completing the channel here would cut it off mid-sentence, so the work already queued is finished
            // first. Work queued after this point has no one left to hear it and is refused.
            _work.Writer.TryComplete();

            await SafeAwaitAsync(_consumer);

            _messages.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Stops the replies that are being spoken or are waiting to be, because the user started talking over them.
    /// </summary>
    /// <param name="message">The partial transcript that revealed the user is speaking.</param>
    private async Task BargeInAsync(RealtimeServerMessage message)
    {
        var interrupted = false;

        lock (_replies)
        {
            foreach (var reply in _replies)
            {
                if (!reply.IsCancellationRequested)
                {
                    reply.Cancel();
                    interrupted = true;
                }
            }
        }

        if (!interrupted)
        {
            return;
        }

        // Mirrors what a native provider emits when it detects speech over its own audio, which is what a
        // caller listens for to drop whatever it has already buffered for playback.
        await _messages.Writer.WriteAsync(new RealtimeServerMessage
        {
            Type = RealtimeServerMessageType.RawContentOnly,
            RawRepresentation = JsonDocument.Parse($$"""{"type":"input_audio_buffer.speech_started","item_id":{{JsonSerializer.Serialize(message.MessageId)}}}""").RootElement.Clone(),
        });
    }

    /// <summary>
    /// Creates the cancellation for a reply that is about to be queued, and registers it so an interruption reaches it.
    /// </summary>
    /// <remarks>
    /// Created when the reply is queued rather than when it starts: an interruption arriving in the moment between
    /// queueing the reply and the reply starting must still be recognized as one.
    /// </remarks>
    private CancellationTokenSource? NewReplyCancellation()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellation.Token);

        lock (_replies)
        {
            _replies.Add(cancellation);
        }

        return cancellation;
    }

    /// <summary>
    /// Queues work for the consumer, without waiting for the work in front of it.
    /// </summary>
    /// <param name="work">The work to queue.</param>
    private void Enqueue(QueuedWork work)
    {
        if (_work.Writer.TryWrite(work))
        {
            return;
        }

        // The session is over, so there is nobody left to answer.
        ReleaseReply(work.Cancellation);
    }

    private void ReleaseReply(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        lock (_replies)
        {
            _replies.Remove(cancellation);
        }

        cancellation.Dispose();
    }

    /// <summary>
    /// Works through the queue one item at a time: the history and the audio timeline are both ordered, so a
    /// reply is finished before the next item is looked at.
    /// </summary>
    /// <param name="cancellationToken">A token that ends the session.</param>
    private async Task ConsumeWorkAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var work in _work.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    if (work.Message is not null)
                    {
                        _history.Add(work.Message);
                    }

                    if (work.Respond && work.Cancellation is not null)
                    {
                        await RunTurnAsync(work.Response, work.Cancellation.Token);
                    }
                }
                finally
                {
                    ReleaseReply(work.Cancellation);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The session was disposed.
        }
    }

    /// <summary>
    /// Converts an item the host adds to the conversation into a chat message, or <see langword="null"/> when it
    /// carries nothing the chat leg can read.
    /// </summary>
    /// <param name="item">The conversation item.</param>
    private static ChatMessage? ToChatMessage(RealtimeConversationItem item)
    {
        var text = string.Concat(item.Contents.OfType<TextContent>().Select(content => content.Text));

        return string.IsNullOrWhiteSpace(text)
            ? null
            : new ChatMessage(item.Role ?? ChatRole.System, text);
    }

    /// <summary>
    /// Generates and speaks one reply.
    /// </summary>
    /// <param name="response">The caller's request for the reply, or <see langword="null"/> for a reply to the user.</param>
    /// <param name="cancellationToken">A token that cancels this reply, including on barge-in.</param>
    private async Task RunTurnAsync(CreateResponseRealtimeClientMessage? response, CancellationToken cancellationToken)
    {
        var responseId = Guid.NewGuid().ToString("n");
        var reply = new StringBuilder();
        var pending = new StringBuilder();

        // Everything the chat leg returns, the tool calls and their results as well as the text, so the next turn is
        // written knowing what this one looked up.
        List<ChatResponseUpdate> updates = [];

        // An out-of-band reply ("let me look that up") is spoken but never becomes part of the conversation.
        var keepInHistory = response?.ExcludeFromConversation != true;

        try
        {
            await WriteAsync(new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseCreated)
            {
                ResponseId = responseId,
            });

            await foreach (var update in _chatClient.GetStreamingResponseAsync(ConversationFor(response), OptionsFor(response), cancellationToken))
            {
                updates.Add(update);

                var text = update.Text;

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                reply.Append(text);
                pending.Append(text);

                await WriteAsync(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioTranscriptionDelta)
                {
                    Text = text,
                    ResponseId = responseId,
                });

                while (TryTakeSpeakableFragment(pending, out var fragment))
                {
                    await SpeakAsync(fragment, responseId, cancellationToken);
                }
            }

            if (pending.Length > 0)
            {
                await SpeakAsync(pending.ToString(), responseId, cancellationToken);
            }

            await WriteAsync(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioTranscriptionDone)
            {
                Text = reply.ToString(),
                ResponseId = responseId,
            });

            if (keepInHistory)
            {
                _history.AddRange(MessagesOf(updates));
            }

            await WriteAsync(new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
            {
                ResponseId = responseId,
                Status = RealtimeResponseStatus.Completed,
            });
        }
        catch (OperationCanceledException)
        {
            // Interrupted by the user or by disposal. Whatever was said before the interruption stays in
            // history, because the user heard it, and so does what was looked up for it.
            if (keepInHistory)
            {
                _history.AddRange(MessagesOf(updates));
            }

            await WriteAsync(new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
            {
                ResponseId = responseId,
                Status = RealtimeResponseStatus.Cancelled,
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The cascaded realtime turn failed.");

            await WriteErrorAsync(exception);
        }
    }

    /// <summary>
    /// The messages a reply adds to the conversation: its tool calls and their results, then its text.
    /// </summary>
    /// <remarks>
    /// Keeping only the text left the next turn blind to what this one looked up. A booking lost the slots it had
    /// found, and a model that saw availability it had no record of checking took the answer back. A call the reply was
    /// interrupted before answering has no result, and providers that pair each call with its result refuse a
    /// conversation holding one, so it is left out.
    /// </remarks>
    /// <param name="updates">Everything the chat leg returned for the reply.</param>
    private static IEnumerable<ChatMessage> MessagesOf(List<ChatResponseUpdate> updates)
    {
        if (updates.Count == 0)
        {
            yield break;
        }

        var messages = updates.ToChatResponse().Messages;
        var answered = messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Select(result => result.CallId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            List<AIContent> contents = [.. message.Contents.Where(content => content is not FunctionCallContent call || answered.Contains(call.CallId))];

            if (contents.Count == 0 || (contents.TrueForAll(content => content is TextContent) && string.IsNullOrWhiteSpace(message.Text)))
            {
                continue;
            }

            yield return new ChatMessage(message.Role, contents)
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId,
            };
        }
    }

    /// <summary>
    /// The conversation a reply is written from: the history under the session's instructions, or under a request's
    /// own for that reply only, as a speech-to-speech provider treats them.
    /// </summary>
    /// <param name="response">The caller's request for the reply, or <see langword="null"/> for a reply to the user.</param>
    private List<ChatMessage> ConversationFor(CreateResponseRealtimeClientMessage? response)
    {
        var instructions = string.IsNullOrWhiteSpace(response?.Instructions) ? _instructions : response.Instructions;

        List<ChatMessage> conversation = [];

        if (instructions is not null)
        {
            conversation.Add(new ChatMessage(ChatRole.System, instructions));
        }

        conversation.AddRange(_history);

        // A reply nobody spoke to ask for, such as a prompt after a silence, can follow the assistant's own last line, and
        // the conversation then ends with the assistant. Models that do not continue an assistant's message refuse that
        // (Claude's 5.x models: "The conversation must end with a user message"), so a short user turn says there is
        // nothing new to answer.
        if (response is not null && conversation.Count > 0 && conversation[^1].Role == ChatRole.Assistant)
        {
            conversation.Add(new ChatMessage(ChatRole.User, NothingNewFromTheUser));
        }

        return conversation;
    }

    /// <summary>
    /// The chat options for a reply: the session's, with what a request limits for its reply alone.
    /// </summary>
    /// <param name="response">The caller's request for the reply, or <see langword="null"/> for a reply to the user.</param>
    private ChatOptions? OptionsFor(CreateResponseRealtimeClientMessage? response)
    {
        if (response?.ToolMode is null && response?.MaxOutputTokens is null)
        {
            return _chatOptions;
        }

        var options = _chatOptions?.Clone() ?? new ChatOptions();
        options.ToolMode = response.ToolMode ?? options.ToolMode;
        options.MaxOutputTokens = response.MaxOutputTokens ?? options.MaxOutputTokens;

        return options;
    }

    /// <summary>
    /// Speaks one fragment of the reply, emitting its audio as it arrives.
    /// </summary>
    /// <param name="text">The fragment to speak.</param>
    /// <param name="responseId">The identifier of the reply this fragment belongs to.</param>
    /// <param name="cancellationToken">A token that cancels this turn.</param>
    private async Task SpeakAsync(string text, string responseId, CancellationToken cancellationToken)
    {
        await foreach (var update in _speechClient.GetStreamingAudioAsync(text, _speechOptions, cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is not DataContent audio || audio.Data.IsEmpty)
                {
                    continue;
                }

                if (!IsRawPcm(audio.MediaType))
                {
                    throw new NotSupportedException($"The text-to-speech deployment answered with '{audio.MediaType}' audio, but a realtime session plays raw PCM ('{PcmMediaType}'). Configure that deployment to produce raw PCM at the session's output sample rate, or choose one that can.");
                }

                await WriteAsync(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioDelta)
                {
                    Audio = Convert.ToBase64String(audio.Data.Span),
                    ResponseId = responseId,
                });
            }
        }
    }

    /// <summary>
    /// Determines whether a media type describes raw, headerless PCM samples.
    /// </summary>
    /// <param name="mediaType">The media type reported by the speech model.</param>
    /// <remarks>
    /// <c>audio/L16</c> is the registered type for linear PCM; <c>audio/pcm</c> is the informal spelling the
    /// realtime APIs use for the same bytes. Both are accepted, and everything else is refused.
    /// </remarks>
    internal static bool IsRawPcm(string? mediaType)
    {
        return string.Equals(mediaType, PcmMediaType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "audio/pcm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Takes the next fragment of the reply that is long enough and complete enough to speak on its own.
    /// </summary>
    /// <param name="pending">The reply text that has not been spoken yet. The fragment is removed from it.</param>
    /// <param name="fragment">The fragment to speak, when one is available.</param>
    internal static bool TryTakeSpeakableFragment(StringBuilder pending, out string fragment)
    {
        ArgumentNullException.ThrowIfNull(pending);

        for (var index = 0; index < pending.Length; index++)
        {
            if (pending[index] is not ('.' or '!' or '?' or '\n'))
            {
                continue;
            }

            // A terminator has to be followed by whitespace (or end the buffer) to end a sentence, which
            // keeps "3.5" and "Dr. Chen" from being split down the middle.
            if (index + 1 < pending.Length && !char.IsWhiteSpace(pending[index + 1]))
            {
                continue;
            }

            var length = index + 1;

            if (length < MinimumSpeakableLength && length < pending.Length)
            {
                continue;
            }

            fragment = pending.ToString(0, length);

            pending.Remove(0, length);

            return true;
        }

        fragment = string.Empty;

        return false;
    }

    private Task WriteAsync(RealtimeServerMessage message)
    {
        // Writing to a channel that is already complete is refused rather than thrown, which is what a reply that is
        // still speaking when the session is disposed needs: what it says then has nobody to reach.
        _messages.Writer.TryWrite(message);

        return Task.CompletedTask;
    }

    private async Task WriteErrorAsync(Exception exception)
    {
        await WriteAsync(new ErrorRealtimeServerMessage
        {
            Error = new ErrorContent(exception.Message),
        });
    }

    private static async Task SafeAwaitAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (Exception)
        {
            // Faults are reported through the message channel; this only waits for the work to stop.
        }
    }

    /// <summary>
    /// One item for the consumer: a message to add to the conversation, a reply to give, or both.
    /// </summary>
    /// <param name="Message">The message to add to the history, or <see langword="null"/> for none.</param>
    /// <param name="Respond">Whether to give a reply once the message is added.</param>
    /// <param name="Response">The caller's request for the reply, or <see langword="null"/> for a reply to the user.</param>
    /// <param name="Cancellation">The cancellation of the reply, or <see langword="null"/> when there is none.</param>
    private sealed record QueuedWork(ChatMessage? Message, bool Respond, CreateResponseRealtimeClientMessage? Response, CancellationTokenSource? Cancellation);
}
