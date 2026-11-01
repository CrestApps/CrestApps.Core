#pragma warning disable MEAI001 // Speech and realtime APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.AI.Cartesia.Services;

/// <summary>
/// A realtime transcription session over one connection to Cartesia's turns endpoint: the speech-to-text leg of a
/// cascaded realtime deployment.
/// </summary>
/// <remarks>
/// <para>
/// Audio appended to the session goes to Cartesia as it arrives: Cartesia decides where each turn ends. It has nothing
/// to flush a turn early with, so committing the input buffer is ignored, as are the other client messages (a session
/// update, a truncation), which mean nothing to a transcription leg. Cartesia expects audio to keep coming, silence
/// included, which a call's audio does.
/// </para>
/// <para>
/// A turn Cartesia leaves open through the silence after its words is ended by the session, a little after Cartesia's own
/// turn timeout would have ended it (see <see cref="CartesiaTurnAssembler"/>).
/// </para>
/// <para>
/// If the connection ends while the session is in use, the session reports an error and ends its stream rather than
/// going deaf without a word, so whoever holds the call can replace the session.
/// </para>
/// </remarks>
internal sealed class CartesiaTranscriptionSession : IRealtimeClientSession
{
    private const int MaximumMessageBytes = 256 * 1024;

    // How long past Cartesia's own turn timeout a turn it left open is ended here: Cartesia hears with a delay of its
    // own, and a turn it ends itself is never cut short.
    private const int StalledTurnMarginMilliseconds = 1000;

    private static readonly TimeSpan _stallCheckInterval = TimeSpan.FromMilliseconds(250);

    private static readonly byte[] _closeMessage = Encoding.UTF8.GetBytes("""{"type":"close"}""");

    private readonly WebSocket _socket;
    private readonly CartesiaTurnAssembler _assembler;
    private readonly int _bytesPerSecond;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<RealtimeServerMessage> _messages = Channel.CreateUnbounded<RealtimeServerMessage>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    // Cartesia's events and the stall check both read the turn and write what it produces; one at a time keeps the
    // messages in order.
    private readonly object _turnGate = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _sessionCancellation = new();
    private readonly Task _receiving;
    private readonly Task _watching;

    private long _audioBytesSent;
    private long _eventsReceived;
    private int _audioRefused;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CartesiaTranscriptionSession"/> class over a connected socket.
    /// </summary>
    /// <param name="socket">The open connection to Cartesia's turns endpoint.</param>
    /// <param name="options">The options the session was created with.</param>
    /// <param name="bytesPerSecond">How many bytes one second of the session's audio takes, to meter it.</param>
    /// <param name="turnEndTimeoutMilliseconds">How long Cartesia was asked to wait at most after speech before it ends a turn.</param>
    /// <param name="logger">The logger, or <see langword="null"/> for none.</param>
    /// <param name="timeProvider">The clock, or <see langword="null"/> for the system's.</param>
    public CartesiaTranscriptionSession(
        WebSocket socket,
        RealtimeSessionOptions options,
        int bytesPerSecond,
        int turnEndTimeoutMilliseconds,
        ILogger logger = null,
        TimeProvider timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        Options = options;
        _bytesPerSecond = bytesPerSecond;
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _assembler = new CartesiaTurnAssembler(
            () => Interlocked.Read(ref _audioBytesSent),
            bytesPerSecond,
            TimeSpan.FromMilliseconds(turnEndTimeoutMilliseconds + StalledTurnMarginMilliseconds),
            _timeProvider,
            _logger);
        _receiving = Task.Run(ReceiveAsync, CancellationToken.None);
        _watching = Task.Run(WatchForStalledTurnsAsync, CancellationToken.None);
    }

    /// <inheritdoc/>
    public RealtimeSessionOptions Options { get; }

    /// <inheritdoc/>
    public async Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (message is InputAudioBufferAppendRealtimeClientMessage { Content: { } audio } && !audio.Data.IsEmpty)
        {
            if (await SendFrameAsync(audio.Data, WebSocketMessageType.Binary, cancellationToken))
            {
                Interlocked.Add(ref _audioBytesSent, audio.Data.Length);
            }
            else if (Interlocked.Exchange(ref _audioRefused, 1) == 0)
            {
                _logger.LogWarning("The caller's audio is not reaching Cartesia: the transcription connection is {State}.", _socket.State);
            }
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(CancellationToken cancellationToken = default)
        => _messages.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc/>
    public object GetService(Type serviceType, object serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            // Cartesia is told the stream is over. Nobody is left to read what it flushes in reply, so the close
            // handshake is not waited for.
            using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await SendFrameAsync(_closeMessage, WebSocketMessageType.Text, closing.Token);
            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Session ended", closing.Token);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            _socket.Abort();
        }

        await _sessionCancellation.CancelAsync();
        await Task.WhenAny(Task.WhenAll(_receiving, _watching), Task.Delay(TimeSpan.FromSeconds(2)));

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Closed the Cartesia transcription session after {Seconds} s of audio and {Events} event(s).",
                Interlocked.Read(ref _audioBytesSent) / _bytesPerSecond, Interlocked.Read(ref _eventsReceived));
        }

        _messages.Writer.TryComplete();
        _socket.Dispose();
        _sessionCancellation.Dispose();
        _sendLock.Dispose();
    }

    private async Task<bool> SendFrameAsync(ReadOnlyMemory<byte> frame, WebSocketMessageType type, CancellationToken cancellationToken)
    {
        // Audio and the closing request come from different threads, and a socket takes one send at a time.
        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            if (_socket.State != WebSocketState.Open)
            {
                return false;
            }

            await _socket.SendAsync(frame, type, endOfMessage: true, cancellationToken);

            return true;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task WatchForStalledTurnsAsync()
    {
        try
        {
            while (!_sessionCancellation.IsCancellationRequested)
            {
                await Task.Delay(_stallCheckInterval, _timeProvider, _sessionCancellation.Token);

                lock (_turnGate)
                {
                    foreach (var message in _assembler.EndStalledTurn())
                    {
                        _messages.Writer.TryWrite(message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The session is ending.
        }
    }

    private async Task ReceiveAsync()
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        string failure = null;

        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                buffer.ResetWrittenCount();
                ValueWebSocketReceiveResult received;

                do
                {
                    received = await _socket.ReceiveAsync(buffer.GetMemory(4096), _sessionCancellation.Token);
                    buffer.Advance(received.Count);

                    if (buffer.WrittenCount > MaximumMessageBytes)
                    {
                        throw new InvalidDataException("A Cartesia message exceeded the size a transcription message can have.");
                    }
                }
                while (!received.EndOfMessage);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (received.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                Interlocked.Increment(ref _eventsReceived);

                lock (_turnGate)
                {
                    foreach (var message in _assembler.Process(buffer.WrittenSpan))
                    {
                        _messages.Writer.TryWrite(message);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_sessionCancellation.IsCancellationRequested)
        {
            // The session is ending.
        }
        catch (Exception exception)
        {
            // Whatever stops the loop leaves the caller unheard, so it is reported below rather than lost with the task.
            failure = exception.Message;
        }

        // A connection that ends while the session is still in use leaves the caller unheard. Saying so lets whoever
        // holds the call replace the session; ending the stream quietly would not.
        if (Volatile.Read(ref _disposed) == 0)
        {
            _logger.LogWarning(
                "Cartesia's transcription stream ended while the session was in use, after {Seconds} s of audio: {Reason}",
                Interlocked.Read(ref _audioBytesSent) / _bytesPerSecond,
                failure ?? $"the connection closed ({_socket.CloseStatus?.ToString() ?? "no status"}: {_socket.CloseStatusDescription}).");

            _messages.Writer.TryWrite(new ErrorRealtimeServerMessage
            {
                Error = new ErrorContent(failure ?? "Cartesia ended the transcription stream."),
            });
        }

        _messages.Writer.TryComplete();
    }
}
