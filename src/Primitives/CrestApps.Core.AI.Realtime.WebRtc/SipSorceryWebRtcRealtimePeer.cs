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
    private const int FrameSamples = SampleRate / 1000 * 20; // 480

    // RTP timestamp increment per 20 ms Opus frame on the negotiated 48 kHz Opus clock.
    private const uint RtpUnitsPerFrame = 960;

    private const int OpusPayloadType = 111;

    private readonly RTCPeerConnection _pc;
    private readonly IOpusDecoder _decoder;
    private readonly IOpusEncoder _encoder;
    private readonly Channel<ReadOnlyMemory<byte>> _incoming;
    private readonly ILogger _logger;

    // Decode scratch (large enough for any single Opus frame at 24 kHz).
    private readonly short[] _decodeBuffer = new short[SampleRate / 1000 * 120];
    private readonly byte[] _encodeOut = new byte[4000];
    private readonly short[] _encodeFrame = new short[FrameSamples];

    // Outgoing accumulator (assistant PCM arrives in arbitrary chunk sizes; Opus needs fixed 20 ms frames).
    private readonly List<short> _encodePending = new(FrameSamples * 8);

    private bool _closedRaised;
    private bool _connectedRaised;
    private long _rtpReceived;
    private long _framesSent;
    private short _recentPeak;

    public string AnswerSdp { get; private set; }

    public event Action<WebRtcIceCandidate> IceCandidateGenerated;
    public event Action Connected;
    public event Action Closed;

    public SipSorceryWebRtcRealtimePeer(IReadOnlyList<WebRtcIceServer> iceServers, ILogger logger)
    {
        _logger = logger;
        _incoming = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        _decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1);
        _encoder = OpusCodecFactory.CreateEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);

        var config = new RTCConfiguration
        {
            iceServers = [.. iceServers.Select(ToRtcIceServer)],
        };

        _pc = new RTCPeerConnection(config);

        var opusFormat = new AudioFormat(AudioCodecsEnum.OPUS, OpusPayloadType, 48000, 2);
        _pc.addTrack(new MediaStreamTrack(opusFormat, MediaStreamStatusEnum.SendRecv));

        _pc.onicecandidate += OnLocalIceCandidate;
        _pc.onconnectionstatechange += OnConnectionStateChanged;
        _pc.oniceconnectionstatechange += OnIceConnectionStateChanged;
        _pc.OnRtpPacketReceived += OnRtpPacketReceived;
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
            _encodePending.Add((short)(span[i] | (span[i + 1] << 8)));
        }

        while (_encodePending.Count >= FrameSamples)
        {
            CollectionsMarshalCopy(_encodePending, _encodeFrame, FrameSamples);
            _encodePending.RemoveRange(0, FrameSamples);

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
                _pc.SendAudio(RtpUnitsPerFrame, _encodeOut.AsSpan(0, encoded).ToArray());

                if (Interlocked.Increment(ref _framesSent) == 1)
                {
                    _logger.LogInformation("WebRTC realtime: first assistant audio frame sent to the browser.");
                }
            }
        }
    }

    private void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket packet)
    {
        if (mediaType != SDPMediaTypesEnum.audio || packet?.Payload is not { Length: > 0 } payload)
        {
            return;
        }

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
        if (_logger.IsEnabled(LogLevel.Information))
        {
            if (count == 1)
            {
                _logger.LogInformation("WebRTC realtime: first inbound audio packet decoded ({Samples} samples @ 24 kHz).", samples);
            }
            else if (count % 250 == 0)
            {
                _logger.LogInformation("WebRTC realtime: inbound audio flowing ({Count} packets, recent peak amplitude {Peak}/32767).", count, _recentPeak);
                _recentPeak = 0;
            }
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

    public ValueTask DisposeAsync()
    {
        RaiseClosedOnce();

        try
        {
            _pc.close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error closing the WebRTC peer connection.");
        }

        return ValueTask.CompletedTask;
    }

    private static RTCIceServer ToRtcIceServer(WebRtcIceServer server) => new()
    {
        urls = string.Join(",", server.Urls ?? []),
        username = server.Username,
        credential = server.Credential,
    };

    private static void CollectionsMarshalCopy(List<short> source, short[] destination, int count)
    {
        for (var i = 0; i < count; i++)
        {
            destination[i] = source[i];
        }
    }
}
