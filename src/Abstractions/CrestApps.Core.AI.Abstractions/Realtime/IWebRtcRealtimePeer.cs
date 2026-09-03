namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// A server-relay WebRTC peer connection to a single browser. It abstracts the WebRTC/Opus transport away from
/// the realtime chat pipeline: audio crosses this boundary as raw PCM16 at 24 kHz (mono) — the same format the
/// realtime runner and provider session use — while Opus, RTP, DTLS/SRTP and ICE are handled inside the
/// implementation. Transcripts and other events do <em>not</em> flow through here; they stay on SignalR.
/// </summary>
public interface IWebRtcRealtimePeer : IAsyncDisposable
{
    /// <summary>
    /// Gets the SDP answer produced for the browser's offer, to be returned over the signaling channel.
    /// </summary>
    string AnswerSdp { get; }

    /// <summary>
    /// Raised when the peer generates a local ICE candidate that must be trickled to the browser.
    /// </summary>
    event Action<WebRtcIceCandidate> IceCandidateGenerated;

    /// <summary>
    /// Raised once the peer's ICE connection is established. The realtime session should start on this signal.
    /// </summary>
    event Action Connected;

    /// <summary>
    /// Raised when the peer connection fails or closes. The realtime session should stop on this signal.
    /// </summary>
    event Action Closed;

    /// <summary>
    /// Adds a remote ICE candidate received from the browser.
    /// </summary>
    void AddIceCandidate(WebRtcIceCandidate candidate);

    /// <summary>
    /// Reads the microphone audio arriving from the browser as PCM16 (24 kHz, mono) frames, for use as the
    /// realtime runner's audio input.
    /// </summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the amount of assistant audio, in milliseconds, that is encoded and queued but has not been sent to
    /// the browser yet. Because outgoing audio is paced to the media clock, this is roughly how much longer the
    /// user will keep hearing the current reply after the provider says it is done.
    /// </summary>
    int QueuedPlaybackMs { get; }

    /// <summary>
    /// Sends assistant audio (PCM16, 24 kHz, mono) to the browser as an Opus track.
    /// </summary>
    void SendAudio(ReadOnlyMemory<byte> pcm24k);

    /// <summary>
    /// Discards any assistant audio still queued for playback. Called on barge-in so the interrupted response
    /// stops immediately instead of playing out the buffered tail while the new response begins.
    /// </summary>
    void FlushPlayback();
}
