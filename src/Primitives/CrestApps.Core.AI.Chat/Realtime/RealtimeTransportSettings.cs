using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Reads the host's realtime transport configuration. Both the realtime hubs and the hosts that render the chat
/// views ask these questions, so the answers cannot drift between what a page advertises and what the hub will
/// actually do.
/// </summary>
public static class RealtimeTransportSettings
{
    /// <summary>
    /// Gets a value indicating whether the server-relay WebRTC transport is available: the peer factory is
    /// registered, and the transport has not been switched off in configuration.
    /// </summary>
    /// <param name="services">The service provider to resolve the peer factory and transport options from.</param>
    public static bool IsWebRtcEnabled(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetService<IWebRtcRealtimePeerFactory>() is null)
        {
            return false;
        }

        var options = services.GetService<IOptions<RealtimeTransportOptions>>()?.Value;

        return options is null || options.EnableWebRtc;
    }

    /// <summary>
    /// Gets how long a realtime session may go without the user speaking before it is ended, or
    /// <see langword="null"/> when the timeout is disabled.
    /// </summary>
    /// <param name="services">The service provider to resolve the transport options from.</param>
    public static TimeSpan? GetIdleTimeout(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var minutes = services.GetService<IOptions<RealtimeTransportOptions>>()?.Value.IdleTimeoutMinutes ?? 0;

        return minutes > 0 ? TimeSpan.FromMinutes(minutes) : null;
    }
}
