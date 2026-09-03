using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Concentus;
using Concentus.Enums;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace CrestApps.Core.AI.Realtime.WebRtc;

/// <summary>
/// A server-relay WebRTC peer backed by SIPSorcery (ICE/DTLS/SRTP/RTP) and Concentus (Opus). Audio crosses the
/// <see cref="IWebRtcRealtimePeer"/> boundary as PCM16 @ 24 kHz mono; Opus encode/decode is configured at 24 kHz
/// so Opus reconciles the browser's native 48 kHz internally — no separate resampling is performed here.
/// </summary>
internal sealed class SipSorceryWebRtcRealtimePeer : IWebRtcRealtimePeer
{
    // Realtime pipeline audio format (matches RealtimeSessionConfiguratorContext defaults).
    private const int SampleRate = 24000;

    // Opus frame we emit toward the browser: 20 ms @ 24 kHz.
    private const int FrameDurationMs = 20;

    private const int FrameSamples = SampleRate / 1000 * FrameDurationMs; // 480

    // Inbound microphone audio is buffered only long enough to absorb a scheduling hiccup. An unbounded buffer
    // grew without limit while the provider session was still opening and then burst several seconds of stale
    // speech at it; capping the buffer and dropping the oldest frames keeps the conversation live instead.
    private const int MaxBufferedInboundFrames = 100; // ~2 s at 20 ms per frame.

    // Upper bound on how many frames one pacing tick may send when the loop woke up late. Catching up matters
    // (Windows timer granularity is ~15.6 ms, so ticks routinely run long), but an unbounded burst would just
    // flood the browser's jitter buffer again.
    private const int MaxCatchUpFramesPerTick = 3;

    // RTP timestamp increment per 20 ms Opus frame on the negotiated 48 kHz Opus clock.
    private const uint RtpUnitsPerFrame = 960;

    // How long the outgoing stream keeps running (on comfort silence) after the last real assistant frame. Long
    // enough to cover the pauses inside and between replies; after that the stream stops until the next reply.
    private const long SilenceTailMs = 10_000;

    private const int OpusPayloadType = 111;

    // Longest run of lost packets worth concealing; beyond this the stream restarted rather than dropped frames.
    private const int MaxConcealedPackets = 5;

    private readonly RTCPeerConnection _pc;
    private readonly IOpusDecoder _decoder;
    private readonly IOpusEncoder _encoder;
    private readonly Channel<ReadOnlyMemory<byte>> _incoming;
    private readonly ILogger _logger;

    // Decode scratch (large enough for any single Opus frame at 24 kHz).
    private readonly short[] _decodeBuffer = new short[SampleRate / 1000 * 120];
    private readonly byte[] _encodeOut = new byte[4000];
    private readonly short[] _encodeFrame = new short[FrameSamples];

    // Outgoing accumulator (assistant PCM arrives in arbitrary chunk sizes; Opus needs fixed 20 ms frames). A ring
    // buffer rather than a list: draining from the front of a list copies the remainder on every single frame,
    // fifty times a second for the length of every reply.
    private readonly short[] _encodePending = new short[FrameSamples * 16];
    private int _encodePendingHead;
    private int _encodePendingCount;

    // Encoded 20 ms Opus frames waiting to go out. They are drained to RTP on a 20 ms wall-clock cadence (see
    // PaceOutgoingAsync) so assistant audio — which the provider delivers faster than real time, in bursts —
    // plays back at natural speed instead of a rushed, sped-up stream. A ConcurrentQueue so a barge-in flush can
    // safely discard the buffered tail from another thread while the pacing loop drains it.
    private readonly ConcurrentQueue<byte[]> _outgoing = new();
    private readonly CancellationTokenSource _pacingCts = new();
    private readonly byte[] _silenceFrame;
    private readonly Task _pacingTask;

    private bool _closedRaised;
    private bool _connectedRaised;
    private long _rtpReceived;
    private long _framesSent;
    private long _inboundDropped;
    private ushort? _lastInboundSequence;
    private short _recentPeak;
    private int _queuedFrames;

    public string AnswerSdp { get; private set; }

    /// <inheritdoc />
    public int QueuedPlaybackMs => Volatile.Read(ref _queuedFrames) * FrameDurationMs;

    public event Action<WebRtcIceCandidate> IceCandidateGenerated;
    public event Action Connected;
    public event Action Closed;

    public SipSorceryWebRtcRealtimePeer(IReadOnlyList<WebRtcIceServer> iceServers, ILogger logger)
    {
        _logger = logger;
        _incoming = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(MaxBufferedInboundFrames)
        {
            SingleReader = true,
            SingleWriter = true,
            // The provider only cares about what the user is saying now. If the reader stalls, dropping the
            // oldest frames keeps latency bounded; blocking the RTP callback would stall the whole peer.
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        _decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1);
        _encoder = OpusCodecFactory.CreateEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);

        var config = new RTCConfiguration
        {
            iceServers = [.. iceServers.Select(ToRtcIceServer)],
        };

        _pc = new RTCPeerConnection(config);

        // Opus always negotiates at 48 kHz, and its SDP rtpmap channel count MUST be 2 for WebRTC: browsers
        // (Chrome, Firefox) always offer "opus/48000/2" per RFC 7587, so advertising "opus/48000/1" here makes
        // SIPSorcery's format matcher reject the offer as AudioIncompatible and the session dies on connect.
        // The stream is still mono — that is signalled by the fmtp "stereo=0" parameter, not the rtpmap count —
        // which is why we encode and decode a single channel above.
        var opusFormat = new AudioFormat(AudioCodecsEnum.OPUS, OpusPayloadType, 48000, 2);
        _pc.addTrack(new MediaStreamTrack(opusFormat, MediaStreamStatusEnum.SendRecv));

        _silenceFrame = EncodeSilenceFrame();

        _pc.onicecandidate += OnLocalIceCandidate;
        _pc.onconnectionstatechange += OnConnectionStateChanged;
        _pc.oniceconnectionstatechange += OnIceConnectionStateChanged;
        _pc.OnRtpPacketReceived += OnRtpPacketReceived;

        _pacingTask = PaceOutgoingAsync(_pacingCts.Token);
    }

    /// <summary>Sets the remote offer and produces the local answer.</summary>
    public async Task InitializeAsync(string offerSdp)
    {
        var setResult = _pc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = offerSdp,
        });

        if (setResult != SetDescriptionResultEnum.OK)
        {
            throw new InvalidOperationException($"WebRTC offer was rejected: {setResult}.");
        }

        var answer = _pc.createAnswer(null);
        await _pc.setLocalDescription(answer);

        AnswerSdp = answer.sdp;
    }

    public void AddIceCandidate(WebRtcIceCandidate candidate)
    {
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.Candidate))
        {
            return;
        }

        _pc.addIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate.Candidate,
            sdpMid = candidate.SdpMid,
            sdpMLineIndex = (ushort)Math.Max(0, candidate.SdpMLineIndex),
        });
    }

    public IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync(CancellationToken cancellationToken = default)
        => _incoming.Reader.ReadAllAsync(cancellationToken);

    public void SendAudio(ReadOnlyMemory<byte> pcm24k)
    {
        var span = pcm24k.Span;
        for (var i = 0; i + 1 < span.Length; i += 2)
        {
            EnqueuePendingSample((short)(span[i] | (span[i + 1] << 8)));
        }

        while (_encodePendingCount >= FrameSamples)
        {
            DequeuePendingFrame(_encodeFrame);

            int encoded;
            try
            {
                encoded = _encoder.Encode(_encodeFrame, FrameSamples, _encodeOut, _encodeOut.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to Opus-encode a realtime audio frame.");
                continue;
            }

            if (encoded > 0)
            {
                // Queue the frame; the pacing loop releases one frame every 20 ms so playback is real time.
                _outgoing.Enqueue(_encodeOut.AsSpan(0, encoded).ToArray());
                Interlocked.Increment(ref _queuedFrames);
            }
        }
    }

    /// <summary>Discards buffered assistant audio so an interrupted response stops immediately on barge-in.</summary>
    public void FlushPlayback()
    {
        while (_outgoing.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _queuedFrames);
        }

        _encodePendingHead = 0;
        _encodePendingCount = 0;
    }

    private void EnqueuePendingSample(short sample)
    {
        if (_encodePendingCount == _encodePending.Length)
        {
            // The accumulator only ever holds a partial frame plus whatever arrived with it; a full buffer means
            // the encoder is not keeping up, and the freshest audio matters more than the oldest.
            _encodePendingHead = (_encodePendingHead + 1) % _encodePending.Length;
            _encodePendingCount--;
        }

        _encodePending[(_encodePendingHead + _encodePendingCount) % _encodePending.Length] = sample;
        _encodePendingCount++;
    }

    private void DequeuePendingFrame(short[] destination)
    {
        for (var i = 0; i < FrameSamples; i++)
        {
            destination[i] = _encodePending[(_encodePendingHead + i) % _encodePending.Length];
        }

        _encodePendingHead = (_encodePendingHead + FrameSamples) % _encodePending.Length;
        _encodePendingCount -= FrameSamples;
    }

    // Releases 20 ms Opus frames at the media clock's rate. The realtime provider produces audio faster than real
    // time and hands it to us in bursts; sending each frame to RTP the instant it was encoded flooded the browser
    // and made the assistant sound sped-up. Pacing fixes the playback rate (and keeps the un-sent tail in our own
    // queue, where a barge-in can flush it).
    //
    // Two details matter beyond "one frame per tick":
    //  * PeriodicTimer ticks late (Windows timer granularity is ~15.6 ms) and sending exactly one frame per tick
    //    loses that slack permanently, so a long reply falls further and further behind the transcript. Frames are
    //    therefore released against elapsed wall-clock time, catching up a bounded number of frames per tick.
    //  * Gaps (an idle moment, or a barge-in flush) are filled with comfort silence rather than simply skipped.
    //    RTP timestamps must stay contiguous with real time; if the stream stops and resumes with contiguous
    //    timestamps the browser's jitter buffer treats the resumed audio as very late and time-stretches the
    //    opening words of the next reply. (SIPSorcery's track timestamp is read-only, so it cannot be advanced
    //    directly — keeping the stream running is the way to keep the clocks aligned.)
    private async Task PaceOutgoingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            var streaming = false;
            var streamStartMs = 0L;
            var framesSentInStream = 0L;
            var lastRealFrameMs = 0L;

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(FrameDurationMs));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var nowMs = clock.ElapsedMilliseconds;

                if (!streaming)
                {
                    // Nothing has been spoken yet (or the tail expired): stay silent until there is real audio.
                    if (_outgoing.IsEmpty)
                    {
                        continue;
                    }

                    streaming = true;
                    streamStartMs = nowMs;
                    framesSentInStream = 0;
                    lastRealFrameMs = nowMs;
                }
                else if (_outgoing.IsEmpty && nowMs - lastRealFrameMs > SilenceTailMs)
                {
                    // A long quiet stretch: stop the stream rather than trickle silence for the rest of the call.
                    streaming = false;

                    continue;
                }

                // How many frames should have been released by now, counting the one due when the stream started.
                var due = ((nowMs - streamStartMs) / FrameDurationMs) + 1;
                var pending = Math.Min(due - framesSentInStream, MaxCatchUpFramesPerTick);

                for (var i = 0; i < pending; i++)
                {
                    var isRealAudio = _outgoing.TryDequeue(out var frame);

                    if (isRealAudio)
                    {
                        Interlocked.Decrement(ref _queuedFrames);
                        lastRealFrameMs = nowMs;
                    }
                    else
                    {
                        frame = _silenceFrame;
                    }

                    framesSentInStream++;

                    if (frame is not { Length: > 0 })
                    {
                        continue;
                    }

                    try
                    {
                        _pc.SendAudio(RtpUnitsPerFrame, frame);

                        if (isRealAudio && Interlocked.Increment(ref _framesSent) == 1)
                        {
                            _logger.LogInformation("WebRTC realtime: first assistant audio frame sent to the browser.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to send a paced realtime audio frame.");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on teardown.
        }
    }

    // Encodes the single 20 ms silence frame reused to keep the outgoing RTP stream contiguous through pauses.
    // Done once during construction: the Opus encoder is also used by SendAudio on the provider pump thread, and
    // Opus encoder state is not safe to touch from two threads at once.
    private byte[] EncodeSilenceFrame()
    {
        try
        {
            var silence = new short[FrameSamples];
            var buffer = new byte[256];
            var encoded = _encoder.Encode(silence, FrameSamples, buffer, buffer.Length);

            return encoded > 0 ? buffer.AsSpan(0, encoded).ToArray() : [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to encode the realtime comfort-silence frame.");

            return [];
        }
    }

    private void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket packet)
    {
        if (mediaType != SDPMediaTypesEnum.audio || packet?.Payload is not { Length: > 0 } payload)
        {
            return;
        }

        // A sequence gap means packets were lost on the way here. Running the decoder in concealment mode for the
        // missing frame lets Opus interpolate across the gap; feeding the next packet straight in instead leaves an
        // audible click and a worse signal for the provider's speech detection.
        var sequenceNumber = packet.Header.SequenceNumber;
        if (_lastInboundSequence.HasValue)
        {
            var expected = (ushort)(_lastInboundSequence.Value + 1);
            var lost = (ushort)(sequenceNumber - expected);

            // Only conceal a small run; a large jump is a stream restart, not loss.
            if (lost is > 0 and <= MaxConcealedPackets)
            {
                for (var i = 0; i < lost; i++)
                {
                    try
                    {
                        _decoder.Decode(null, _decodeBuffer, FrameSamples, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Opus packet-loss concealment failed for a realtime audio gap.");
                        break;
                    }
                }
            }
        }

        _lastInboundSequence = sequenceNumber;

        int samples;
        try
        {
            samples = _decoder.Decode(payload, _decodeBuffer, _decodeBuffer.Length, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to Opus-decode an incoming realtime audio packet.");
            return;
        }

        if (samples <= 0)
        {
            return;
        }

        var count = Interlocked.Increment(ref _rtpReceived);

        var bytes = new byte[samples * 2];
        short peak = 0;
        for (var i = 0; i < samples; i++)
        {
            var sample = _decodeBuffer[i];
            var magnitude = sample == short.MinValue ? short.MaxValue : Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = (short)magnitude;
            }

            bytes[i * 2] = (byte)(sample & 0xFF);
            bytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        if (peak > _recentPeak)
        {
            _recentPeak = peak;
        }

        // Emit periodic inbound diagnostics: whether audio keeps flowing and how loud it reaches the provider
        // (peak amplitude out of 32767). A near-zero peak while the user is speaking means the mic/AEC is
        // gating the signal to silence, which explains the model not detecting speech.
        if (count == 1 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("WebRTC realtime: first inbound audio packet decoded ({Samples} samples @ 24 kHz).", samples);
        }
        else if (count % 250 == 0 && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("WebRTC realtime: inbound audio flowing ({Count} packets, recent peak amplitude {Peak}/32767).", count, _recentPeak);
            _recentPeak = 0;
        }

        // The channel drops the oldest frame when it is full, so a write never fails here — but a persistently
        // full buffer means the provider send path is not keeping up, which is worth saying out loud.
        if (_incoming.Reader.Count >= MaxBufferedInboundFrames && Interlocked.Increment(ref _inboundDropped) % 50 == 1)
        {
            _logger.LogWarning(
                "WebRTC realtime: the inbound microphone buffer is full ({Frames} frames); dropping the oldest audio because the provider send path is not keeping up.",
                MaxBufferedInboundFrames);
        }

        _incoming.Writer.TryWrite(bytes);
    }

    private void OnLocalIceCandidate(RTCIceCandidate candidate)
    {
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.candidate))
        {
            return;
        }

        IceCandidateGenerated?.Invoke(new WebRtcIceCandidate
        {
            Candidate = candidate.candidate,
            SdpMid = candidate.sdpMid,
            SdpMLineIndex = candidate.sdpMLineIndex,
        });
    }

    private void OnConnectionStateChanged(RTCPeerConnectionState state)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("WebRTC realtime: connection state {State}.", state);
        }

        switch (state)
        {
            case RTCPeerConnectionState.connected:
                RaiseConnectedOnce();
                break;

            case RTCPeerConnectionState.failed:
            case RTCPeerConnectionState.closed:
                RaiseClosedOnce();
                break;
        }
    }

    private void OnIceConnectionStateChanged(RTCIceConnectionState state)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("WebRTC realtime: ICE connection state {State}.", state);
        }

        // Some flows surface a usable connection via the ICE state before the aggregate connection state; treat
        // ICE connected as connected so the realtime session starts promptly.
        if (state is RTCIceConnectionState.connected)
        {
            RaiseConnectedOnce();
        }
        else if (state is RTCIceConnectionState.failed or RTCIceConnectionState.closed)
        {
            RaiseClosedOnce();
        }
    }

    private void RaiseConnectedOnce()
    {
        if (_connectedRaised)
        {
            return;
        }

        _connectedRaised = true;
        _logger.LogInformation("WebRTC realtime: peer connected.");
        Connected?.Invoke();
    }

    private void RaiseClosedOnce()
    {
        if (_closedRaised)
        {
            return;
        }

        _closedRaised = true;
        _incoming.Writer.TryComplete();
        Closed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        RaiseClosedOnce();

        try
        {
            await _pacingCts.CancelAsync().ConfigureAwait(false);
            await _pacingTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error stopping the realtime audio pacing loop.");
        }
        finally
        {
            _pacingCts.Dispose();
        }

        try
        {
            _pc.close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error closing the WebRTC peer connection.");
        }
    }

    private static RTCIceServer ToRtcIceServer(WebRtcIceServer server) => new()
    {
        urls = string.Join(",", server.Urls ?? []),
        username = server.Username,
        credential = server.Credential,
    };

}
