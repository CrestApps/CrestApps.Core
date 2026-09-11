namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Supplies the ICE (STUN/TURN) servers used to negotiate a realtime WebRTC connection.
/// </summary>
/// <remarks>
/// <para>
/// Both halves of a realtime session resolve their servers here — the list handed to the browser and the one
/// the server-relay peer connects with — so the two can never disagree about which relay to use or which
/// credentials to present.
/// </para>
/// <para>
/// The method is asynchronous because a TURN provider is entitled to fetch credentials over the network.
/// Hosted TURN services (Cloudflare Realtime, Twilio) mint short-lived credentials through an HTTP API rather
/// than exposing a shared secret an implementation could sign locally. An implementation that needs no I/O
/// should return a completed <see cref="ValueTask{TResult}"/>, which costs no allocation.
/// </para>
/// </remarks>
public interface IRealtimeIceServerProvider
{
    /// <summary>
    /// Gets the ICE servers to negotiate the next realtime connection with.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The ICE servers, most preferred first. Never <see langword="null"/>; an implementation that cannot
    /// resolve a TURN server returns whatever STUN servers it has rather than failing, because a direct
    /// connection still succeeds for most callers.
    /// </returns>
    ValueTask<IReadOnlyList<WebRtcIceServer>> GetIceServersAsync(CancellationToken cancellationToken = default);
}
