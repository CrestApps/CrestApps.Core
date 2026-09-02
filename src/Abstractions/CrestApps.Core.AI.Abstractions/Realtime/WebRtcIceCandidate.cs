namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// A single ICE candidate exchanged between the browser and the server-relay WebRTC peer during signaling.
/// </summary>
public sealed class WebRtcIceCandidate
{
    /// <summary>
    /// Gets or sets the ICE candidate line (the value of the SDP <c>a=candidate:</c> attribute).
    /// </summary>
    public string Candidate { get; set; }

    /// <summary>
    /// Gets or sets the media stream identification tag the candidate belongs to.
    /// </summary>
    public string SdpMid { get; set; }

    /// <summary>
    /// Gets or sets the index of the media description the candidate belongs to.
    /// </summary>
    public int SdpMLineIndex { get; set; }
}
