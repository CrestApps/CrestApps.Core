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
/// </remarks>
internal sealed class CascadedRealtimeSession : IRealtimeClientSession
{
    // A reply is spoken a sentence at a time so the first audio starts playing while the model is still
    // writing. Below this length a fragment is held back and joined to the next one: sending two or three
    // words to a speech model produces clipped, oddly-cadenced audio.
    private const int MinimumSpeakableLength = 24;

    private readonly IRealtimeClientSession _transcription;
    private readonly IChatClient _chatClient;
    private readonly ITextToSpeechClient _speechClient;
    private readonly ChatOptions? _chatOptions;
    private readonly TextToSpeechOptions _speechOptions;
    private readonly List<ChatMessage> _history = [];
    private readonly Channel<RealtimeServerMessage> _messages = Channel.CreateUnbounded<RealtimeServerMessage>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly CancellationTokenSource _sessionCancellation = new();
    private readonly ILogger _logger;

    private Task? _pump;
    private Task _turn = Task.CompletedTask;
    private CancellationTokenSource? _turnCancellation;
    private bool _speaking;
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

        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            _history.Add(new ChatMessage(ChatRole.System, options!.Instructions));
        }
    }

    /// <summary>
    /// Starts relaying the transcription session. Called once, by the client that created this session.
    /// </summary>
    public void Start()
    {
        _pump ??= Task.Run(() => PumpTranscriptionAsync(_sessionCancellation.Token));
    }

    /// <inheritdoc />
    public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Everything the caller sends is about the user's audio, which only the transcription leg can act
        // on. It decides for itself what it cannot honor.
        return _transcription.SendAsync(message, cancellationToken);
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

        CancelTurn();

        // The pump and the turn own the inner clients while they run, so let them unwind before disposing.
        await SafeAwaitAsync(_pump);
        await SafeAwaitAsync(_turn);

        await _transcription.DisposeAsync();

        _chatClient.Dispose();
        _speechClient.Dispose();
        _sessionCancellation.Dispose();
        _turnCancellation?.Dispose();
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
                    await StartTurnAsync(transcription.Transcription!, cancellationToken);
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
            // The reply to the last thing the user said is still being written and spoken on its own task.
            // Completing the channel here would cut it off mid-sentence.
            await SafeAwaitAsync(_turn);

            _messages.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Stops a reply that is still being spoken because the user started talking over it.
    /// </summary>
    /// <param name="message">The partial transcript that revealed the user is speaking.</param>
    private async Task BargeInAsync(RealtimeServerMessage message)
    {
        if (!_speaking)
        {
            return;
        }

        _speaking = false;

        CancelTurn();

        // Mirrors what a native provider emits when it detects speech over its own audio, which is what a
        // caller listens for to drop whatever it has already buffered for playback.
        await _messages.Writer.WriteAsync(new RealtimeServerMessage
        {
            Type = RealtimeServerMessageType.RawContentOnly,
            RawRepresentation = JsonDocument.Parse($$"""{"type":"input_audio_buffer.speech_started","item_id":{{JsonSerializer.Serialize(message.MessageId)}}}""").RootElement.Clone(),
        });
    }

    /// <summary>
    /// Queues an assistant turn for the utterance the user just finished.
    /// </summary>
    /// <param name="userText">The transcript of the user's utterance.</param>
    /// <param name="cancellationToken">A token that ends the session.</param>
    private async Task StartTurnAsync(string userText, CancellationToken cancellationToken)
    {
        // Turns are strictly sequential: the history and the audio timeline are both ordered, so a new
        // utterance waits for the previous reply to finish unwinding before it starts its own.
        await SafeAwaitAsync(_turn);

        _turnCancellation?.Dispose();
        _turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var turnCancellation = _turnCancellation.Token;

        // Marked here, on the pump, rather than inside the turn: an interruption arriving in the moment
        // between queueing the reply and the reply starting would otherwise not be recognized as one.
        _speaking = true;

        _turn = Task.Run(() => RunTurnAsync(userText, turnCancellation), CancellationToken.None);
    }

    /// <summary>
    /// Generates and speaks one reply.
    /// </summary>
    /// <param name="userText">The transcript of the user's utterance.</param>
    /// <param name="cancellationToken">A token that cancels this turn, including on barge-in.</param>
    private async Task RunTurnAsync(string userText, CancellationToken cancellationToken)
    {
        var responseId = Guid.NewGuid().ToString("n");
        var reply = new StringBuilder();
        var pending = new StringBuilder();

        try
        {
            _history.Add(new ChatMessage(ChatRole.User, userText));

            await WriteAsync(new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseCreated)
            {
                ResponseId = responseId,
            });

            await foreach (var update in _chatClient.GetStreamingResponseAsync(_history, _chatOptions, cancellationToken))
            {
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

            if (reply.Length > 0)
            {
                _history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString()));
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
            // history, because the user heard it.
            if (reply.Length > 0)
            {
                _history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString()));
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
        finally
        {
            _speaking = false;
        }
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

                await WriteAsync(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioDelta)
                {
                    Audio = Convert.ToBase64String(audio.Data.Span),
                    ResponseId = responseId,
                });
            }
        }
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

    private void CancelTurn()
    {
        try
        {
            _turnCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The turn already finished and its source was replaced.
        }
    }

    private async Task WriteAsync(RealtimeServerMessage message)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        await _messages.Writer.WriteAsync(message);
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
}
