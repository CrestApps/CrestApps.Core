namespace CrestApps.Core.AI.Chat.AGUI;

/// <summary>
/// The cancellation of an AG-UI run. A stop the user asks for ends the run at once; a dropped connection (the page
/// closed or opened another page) lets the run go on for a while, so its answer is still saved.
/// </summary>
internal sealed class AGUIRunCancellation : IDisposable
{
    // How long to wait, after the stream was cancelled, for the connection to report that it dropped. SignalR cancels
    // both, in no fixed order.
    private static readonly TimeSpan _disconnectGrace = TimeSpan.FromMilliseconds(250);

    private readonly CancellationTokenSource _source = new();
    private readonly CancellationTokenRegistration _registration;
    private readonly CancellationToken _connectionAborted;

    /// <summary>
    /// Initializes a new instance of the <see cref="AGUIRunCancellation"/> class for a run with no connection: the
    /// run ends when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="cancellationToken">The token that ends the run.</param>
    public AGUIRunCancellation(CancellationToken cancellationToken)
    {
        _registration = cancellationToken.Register(static state => ((CancellationTokenSource)state).Cancel(), _source);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AGUIRunCancellation"/> class for a run on a connection.
    /// </summary>
    /// <param name="disconnectedRunTimeout">How long the run goes on after the connection dropped.</param>
    /// <param name="streamToken">The token of the stream, cancelled by a stop and by a dropped connection.</param>
    /// <param name="connectionAborted">The token cancelled when the connection drops.</param>
    public AGUIRunCancellation(
        TimeSpan disconnectedRunTimeout,
        CancellationToken streamToken,
        CancellationToken connectionAborted)
    {
        _connectionAborted = connectionAborted;
        _registration = streamToken.Register(() => _ = OnStreamCancelledAsync(disconnectedRunTimeout, connectionAborted));
    }

    /// <summary>
    /// Gets the token of the run.
    /// </summary>
    public CancellationToken Token => _source.Token;

    /// <summary>
    /// Gets a value indicating whether the connection the run started on has dropped.
    /// </summary>
    public bool ConnectionLost => _connectionAborted.IsCancellationRequested;

    /// <summary>
    /// Releases the registration and the token source.
    /// </summary>
    public void Dispose()
    {
        _registration.Dispose();
        _source.Dispose();
    }

    private async Task OnStreamCancelledAsync(TimeSpan disconnectedRunTimeout, CancellationToken connectionAborted)
    {
        try
        {
            if (!connectionAborted.IsCancellationRequested)
            {
                await Task.Delay(_disconnectGrace, connectionAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // The connection dropped while waiting.
        }

        try
        {
            if (connectionAborted.IsCancellationRequested && disconnectedRunTimeout > TimeSpan.Zero)
            {
                _source.CancelAfter(disconnectedRunTimeout);
            }
            else
            {
                _source.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
            // The run ended already.
        }
    }
}
