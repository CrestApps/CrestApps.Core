using System.Collections.Concurrent;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default singleton implementation of <see cref="IExternalChatRelayManager"/> that tracks
/// active <see cref="IExternalChatRelay"/> connections by session ID using a thread-safe
/// concurrent dictionary. Relays are created on demand and disposed when closed.
/// </summary>
public sealed class ExternalChatRelayConnectionManager : IExternalChatRelayManager, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, IExternalChatRelay> _relays = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ExternalChatRelayConnectionManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalChatRelayConnectionManager"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public ExternalChatRelayConnectionManager(ILogger<ExternalChatRelayConnectionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets or create.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="context">The context.</param>
    /// <param name="factory">The factory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IExternalChatRelay> GetOrCreateAsync(
        string sessionId,
        ExternalChatRelayContext context,
        Func<IExternalChatRelay> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(factory);

        if (_relays.TryGetValue(sessionId, out var existing) && await existing.IsConnectedAsync(cancellationToken))
        {
            return existing;
        }

        var relay = factory();

        try
        {
            await relay.ConnectAsync(context, cancellationToken);
        }
        catch
        {
            await relay.DisposeAsync();
            throw;
        }

        // If another thread raced us, dispose the new relay and use the winner.
        if (!_relays.TryAdd(sessionId, relay))
        {
            await relay.DisposeAsync();

return _relays[sessionId];
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("External chat relay connected for session '{SessionId}'.", sessionId);
        }

        return relay;
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public IExternalChatRelay Get(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _relays.TryGetValue(sessionId, out var relay);

return relay;
    }

    /// <summary>
    /// Closes the operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task CloseAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_relays.TryRemove(sessionId, out var relay))
        {
            return;
        }

        try
        {
            await relay.DisconnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disconnecting external chat relay for session '{SessionId}'.", sessionId);
        }
        finally
        {
            await relay.DisposeAsync();
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("External chat relay closed for session '{SessionId}'.", sessionId);
        }
    }

    /// <summary>
    /// Disposes the operation.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _relays)
        {
            try
            {
                await kvp.Value.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting relay for session '{SessionId}' during disposal.", kvp.Key);
            }
            finally
            {
                await kvp.Value.DisposeAsync();
            }
        }

        _relays.Clear();
    }
}
