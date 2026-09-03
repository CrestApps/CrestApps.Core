#pragma warning disable MEAI001 // The realtime types from Microsoft.Extensions.AI are for evaluation purposes only.
#nullable enable
using System.Buffers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.OpenAI.Azure.Realtime;

/// <summary>
/// An <see cref="IRealtimeClientSession"/> backed by a raw <see cref="WebSocket"/> connected to the Azure OpenAI
/// realtime endpoint. See <see cref="AzureRealtimeProtocol"/> for the temporary-transport rationale.
/// </summary>
internal sealed class AzureRealtimeClientSession : IRealtimeClientSession
{
    private const int ReceiveBufferSize = 16 * 1024;

    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _disposed;

    /// <inheritdoc />
    public RealtimeSessionOptions? Options { get; private set; }

    public AzureRealtimeClientSession(WebSocket socket, RealtimeSessionOptions? options)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        Options = options;
    }

    /// <inheritdoc />
    public async Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        if (message is SessionUpdateRealtimeClientMessage sessionUpdate)
        {
            Options = sessionUpdate.Options;
        }

        var payload = AzureRealtimeProtocol.WriteClientMessage(message);
        if (payload is null)
        {
            return;
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(payload.Value, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rented = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                using var accumulator = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    var aborted = false;
                    try
                    {
                        result = await _socket.ReceiveAsync(rented.AsMemory(), cancellationToken);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException or SocketException)
                    {
                        // The session is being torn down (typically because the user stopped the conversation)
                        // or the underlying socket was aborted. End the stream gracefully instead of surfacing a
                        // transport exception — a user-requested stop is not an error.
                        aborted = true;
                        result = default;
                    }

                    if (aborted)
                    {
                        yield break;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // An abnormal close carries the only explanation the caller will ever get for the session
                        // disappearing (expired credentials, a rate limit, the provider's session cap). Surfacing
                        // it as an error event makes it both diagnosable in logs and visible to the user, instead
                        // of the conversation simply going quiet.
                        var closeStatus = _socket.CloseStatus;
                        if (closeStatus is not null and not WebSocketCloseStatus.NormalClosure and not WebSocketCloseStatus.Empty)
                        {
                            yield return new ErrorRealtimeServerMessage
                            {
                                Error = new ErrorContent(
                                    $"The realtime connection closed unexpectedly ({closeStatus}): {_socket.CloseStatusDescription ?? "no detail provided"}.")
                                {
                                    ErrorCode = closeStatus.ToString(),
                                },
                            };
                        }

                        yield break;
                    }

                    accumulator.Write(rented, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (accumulator.Length == 0)
                {
                    continue;
                }

                yield return AzureRealtimeProtocol.ReadServerMessage(accumulator.GetBuffer().AsSpan(0, (int)accumulator.Length));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // The connection may already be faulted; disposing below is sufficient.
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _socket.Dispose();
            _sendLock.Dispose();
        }
    }
}
