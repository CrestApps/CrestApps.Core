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
/// <see cref="IWebRtcRealtimePeer"/> boundary as PCM16 @ 24 kHz mono. Assistant audio is Opus-encoded at 24 kHz
/// (the browser decodes that correctly at its native 48 kHz); microphone audio is decoded at 48 kHz and
/// downsampled here — see <see cref="OpusDecodeRate"/> for why it cannot be decoded at 24 kHz directly.
/// </summary>
internal sealed class SipSorceryWebRtcRealtimePeer : IWebRtcRealtimePeer
{
    // Realtime pipeline audio format (matches RealtimeSessionConfiguratorContext defaults).
    private const int SampleRate = 24000;

    // Inbound Opus is decoded at the codec's native rate and downsampled to the pipeline rate afterwards. Asking
    // Concentus to decode straight to 24 kHz was the single biggest defect in the feature: its internal decoder
    // resampler attenuates the output by roughly 40 dB, so a full-scale voice reached the provider at about
    // -37 dBFS — below its speech detection — and "Listening…" never answered anyone who did not shout. Decoding at
    // 48 kHz is exact; the 2:1 decimation below is ours.
    private const int OpusDecodeRate = 48000;

    // Opus frame we emit toward the browser: 20 ms @ 24 kHz.
    private const int FrameDurationMs = 20;

    private const int FrameSamples = SampleRate / 1000 * FrameDurationMs; // 480

    // Inbound microphone audio is buffered only long enough to absorb a scheduling hiccup. An unbounded buffer
    // grew without limit while the provider session was still opening and then burst several seconds of stale
    // speech at it; capping the buffer and dropping the oldest frames keeps the conversation live instead.
    private const int MaxBufferedInboundFrames = 100; // ~2 s at 20 ms per frame.

    // A pacing slot missed by this much or more (the pacing thread was not scheduled — a GC pause, a saturated
    // machine) is not paid back: the slots that went by are given up so the stream stays on time. Paying a long
    // stall back as a burst, or running late from then on, is exactly what makes the browser's jitter buffer
    // time-stretch speech.
    private const long MaxStallDebtMs = 100;

    // RTP timestamp increment per 20 ms Opus frame on the negotiated 48 kHz Opus clock.
    private const uint RtpUnitsPerFrame = 960;

    // Pre-buffer before a reply's first frame is released: wait for this many frames, or this long, whichever
    // comes first. The provider delivers audio in bursts of up to half a second with pauses between them; the
    // cushion is what keeps those pauses from reaching the browser. (See PaceOutgoing.)
    private const int PrimeFrames = 15; // 300 ms
    private const long PrimeMaxWaitMs = 400;

    // Shorter cap on the pre-buffer when a reply resumes after the provider stalled mid-way: the listener is
    // already waiting inside a gap, so the cushion is rebuilt only as far as it can be quickly.
    private const long ResumePrimeMaxWaitMs = 200;

    // Quiet stretch after which the next audio counts as a new reply rather than the rest of the current one.
    private const long SpurtGapMs = 250;

    // A queue that runs dry within this long of the last real frame is a mid-reply gap (audible, counted);
    // beyond it the reply is simply over and silence is the right filler.
    private const long ReplyContinuityMs = 400;

    // How long a mid-reply gap is left to the browser's packet-loss concealment before comfort silence is sent.
    private const long ConcealedGapMs = 80;

    // Payload type this side offers for Opus. The browser is the offerer, so the number actually sent is whatever
    // its offer named (Chrome says 111, Firefox 109) — see ResolveOpusPayloadType.
    private const int OpusPayloadType = 111;

    // Longest run of lost packets worth concealing; beyond this the stream restarted rather than dropped frames.
    private const int MaxConcealedPackets = 5;

    private readonly RTCPeerConnection _pc;
    private readonly IOpusDecoder _decoder;
    private readonly IOpusEncoder _encoder;
    private readonly Channel<ReadOnlyMemory<byte>> _incoming;
    private readonly ILogger _logger;

    // Decode scratch (large enough for any single Opus frame at the decode rate).
    private readonly short[] _decodeBuffer = new short[OpusDecodeRate / 1000 * 120];
    private readonly Pcm48To24Decimator _decimator = new();
    private readonly short[] _decimated = new short[SampleRate / 1000 * 120];
    private readonly byte[] _encodeOut = new byte[4000];
    private readonly short[] _encodeFrame = new short[FrameSamples];

    // The partial 20 ms frame carried over between provider chunks lives in _encodeFrame; this is how much of it
    // is filled. (Assistant PCM arrives in arbitrary chunk sizes; Opus needs fixed 20 ms frames.)
    private int _encodePendingCount;

    // Accounting that proves nothing is dropped between the provider and the wire: samples handed to SendAudio
    // versus samples encoded, and the largest single chunk seen.
    private long _samplesReceived;
    private long _samplesEncoded;
    private int _largestChunkSamples;

    // Encoded 20 ms Opus frames waiting to go out. They are drained to RTP on a 20 ms wall-clock cadence (see
    // PaceOutgoing) so assistant audio — which the provider delivers faster than real time, in bursts —
    // plays back at natural speed instead of a rushed, sped-up stream. A ConcurrentQueue so a barge-in flush can
    // safely discard the buffered tail from another thread while the pacing loop drains it.
    private readonly ConcurrentQueue<byte[]> _outgoing = new();
    private readonly CancellationTokenSource _pacingCts = new();
    private readonly Thread _pacingThread;
    private readonly byte[] _silenceFrame;

    private bool _closedRaised;
    private bool _connectedRaised;
    private long _midReplySilenceFrames;
    private long _stallSlotsSkipped;
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

        _decoder = OpusCodecFactory.CreateDecoder(OpusDecodeRate, 1);
        _encoder = OpusCodecFactory.CreateEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);

        // The encoder's defaults land at ~16 kbps for 24 kHz mono voice in Opus's hybrid mode — telephone quality,
        // with smeared consonants that users heard as the assistant "reading words partially". The provider hands
        // us clean 24 kHz PCM; keep it, and keep it uniform:
        //  - CELT only (the transform half of Opus, what it uses for music): a waveform codec with one consistent
        //    timbre, where the hybrid mode's SILK layer re-synthesises everything below 8 kHz with a quality that
        //    varies from phoneme to phoneme.
        //  - Inter-frame prediction off, so every frame decodes on its own. The stream is not continuous from the
        //    decoder's point of view — it sees pre-encoded comfort silence before each reply and across gaps, and
        //    packet-loss concealment where a slot went unsent — and a frame that is delta-coded against a state
        //    the decoder never saw comes out at the wrong level (measured: error as large as the signal itself in
        //    the first frames after such a splice; with prediction off, ~15 dB below it).
        //  - No in-band FEC: it exists only in the SILK modes, and asking for it with an expected loss rate is what
        //    pushed the encoder into hybrid mode in the first place. Nothing was lost in any measurement; the
        //    browser's concealment covers the rare packet that is.
        // 96 kbps VBR measures ~70 kbps average on speech; the highest complexity is cheap at this rate.
        _encoder.ForceMode = OpusMode.MODE_CELT_ONLY;
        _encoder.PredictionDisabled = true;
        _encoder.Bitrate = 96000;
        _encoder.Complexity = 10;
        _encoder.UseInbandFEC = false;
        _encoder.PacketLossPercent = 0;

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

        // A dedicated thread, not the thread pool: the pool is what a build or a test run in the same process
        // starves, and an audio pacer that is late by a scheduler quantum is an audio pacer that bursts.
        _pacingThread = new Thread(PaceOutgoing)
        {
            IsBackground = true,
            Name = "realtime-audio-pacer",
            Priority = ThreadPriority.AboveNormal,
        };
        _pacingThread.Start();
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
        // Every sample the provider sends is encoded, in order, with nothing dropped and nothing altered: frames
        // are cut as soon as 20 ms is available, and only the partial frame at the end of a chunk is carried over
        // to the next one. A previous version pushed the whole chunk through a 320 ms ring buffer before
        // encoding any of it, and the ring discarded the oldest samples when full — so every provider chunk
        // longer than 320 ms lost its opening, which listeners heard as speech jumping ahead ("speeding up").
        var span = pcm24k.Span;
        var sampleCount = span.Length / 2;
        Interlocked.Add(ref _samplesReceived, sampleCount);
        if (sampleCount > _largestChunkSamples)
        {
            _largestChunkSamples = sampleCount;
        }

        var i = 0;
        while (i < sampleCount)
        {
            // Fill the partial frame carried over from the previous chunk first.
            while (_encodePendingCount < FrameSamples && i < sampleCount)
            {
                _encodeFrame[_encodePendingCount++] = (short)(span[2 * i] | (span[2 * i + 1] << 8));
                i++;
            }

            if (_encodePendingCount < FrameSamples)
            {
                break;
            }

            _encodePendingCount = 0;
            EncodeAndQueueFrame(_encodeFrame);
        }
    }

    private void EncodeAndQueueFrame(short[] frame)
    {
        int encoded;
        try
        {
            encoded = _encoder.Encode(frame, FrameSamples, _encodeOut, _encodeOut.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to Opus-encode a realtime audio frame.");
            return;
        }

        if (encoded > 0)
        {
            // Queue the frame; the pacing loop releases one frame every 20 ms so playback is real time.
            _outgoing.Enqueue(_encodeOut.AsSpan(0, encoded).ToArray());
            Interlocked.Increment(ref _queuedFrames);
            Interlocked.Add(ref _samplesEncoded, FrameSamples);
        }
    }

    /// <summary>Discards buffered assistant audio so an interrupted response stops immediately on barge-in.</summary>
    public void FlushPlayback()
    {
        while (_outgoing.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _queuedFrames);
        }

        _encodePendingCount = 0;
    }

    // Releases 20 ms Opus frames at the media clock's rate, from a dedicated thread. The realtime provider produces
    // audio faster than real time and hands it to us in bursts; sending each frame to RTP the instant it was
    // encoded flooded the browser and made the assistant sound sped-up. Pacing fixes the playback rate (and keeps
    // the un-sent tail in our own queue, where a barge-in can flush it).
    //
    // What the browser's jitter buffer hears is the *timing* of packets, and it reacts to irregular timing by
    // adding delay and then time-stretching speech to shrink it again — which users hear as words speeding up or
    // clipping. So the sender's cadence has to be even, and that rules out timer callbacks: a PeriodicTimer on
    // Windows fires on a ~15.6 ms grid, so a 20 ms period actually arrives as 16/31/16/31 ms and a catch-up burst
    // of 2-3 frames every other tick, and it runs on the thread pool, which the same process starves whenever a
    // build or a test run is going on. This loop instead runs on its own above-normal-priority thread, asks
    // Windows for 1 ms timer resolution while a peer is alive, sleeps to just short of each frame's deadline and
    // spins the last stretch, and sends exactly one frame per 20 ms slot. A slot that is missed by a lot (a stall)
    // is caught up one extra frame at a time, never as a burst, and a debt larger than a few frames is written off
    // rather than paid back all at once.
    //
    // Three further details:
    //  * Once the stream has started it never stops: quiet stretches carry comfort silence for the rest of the
    //    session. RTP timestamps must stay contiguous with real time; a stream that stops and resumes with
    //    contiguous timestamps makes the jitter buffer treat the resumed audio as very late and time-stretch the
    //    opening words of the next reply. (SIPSorcery's track timestamp is read-only, so it cannot be advanced
    //    directly — keeping the stream running is the way to keep the clocks aligned. A silence frame is a few
    //    bytes, fifty times a second.)
    //  * A reply is pre-buffered before its first frame goes out. The provider delivers audio in bursts and the
    //    loop feeding this queue also persists turns and sends transcripts, so a reply's first few hundred
    //    milliseconds regularly ran the queue dry — and every dry slot sent 20 ms of silence into the middle of a
    //    word. Waiting for a small cushion (or a short deadline, so brief replies are not delayed) removes most of
    //    those holes at the cost of ~100 ms of latency.
    //  * A mid-reply underrun skips a few slots before falling back to silence. Late frames then arrive with their
    //    timestamps intact and the jitter buffer stretches the preceding audio to cover the gap, which sounds like
    //    nothing; a silence frame sounds like a dropped syllable.
    private void PaceOutgoing()
    {
        var token = _pacingCts.Token;
        var highResolution = OperatingSystem.IsWindows() && TryBeginHighResolutionTimer();

        try
        {
            var clock = Stopwatch.StartNew();
            var streaming = false;
            var nextDueMs = 0L;
            var lastRealFrameMs = -1L;
            var emptySinceMs = -1L;
            var priming = false;
            var primingSinceMs = 0L;
            var primingMaxWaitMs = PrimeMaxWaitMs;
            var provisionalHoleFrames = 0L;
            var markNext = true;
            var payloadType = OpusPayloadType;

            // The RTP timestamp is driven by the wall clock, not by what was sent: every 20 ms slot owns one frame's
            // worth of timestamp whether a frame went out in it or not. A frame therefore never arrives late
            // relative to its own timestamp, so the browser's jitter buffer never has to speed speech up to catch
            // up afterwards. A slot nothing was sent in is a short gap the browser conceals and then forgets, not a
            // delay the rest of the reply carries. The audio itself is never resampled, stretched or trimmed here.
            var timestamp = (uint)Random.Shared.Next();

            while (!token.IsCancellationRequested)
            {
                if (!streaming)
                {
                    // Nothing has been spoken yet: stay silent until the first reply starts to arrive.
                    if (_outgoing.IsEmpty)
                    {
                        Thread.Sleep(5);

                        continue;
                    }

                    streaming = true;
                    payloadType = ResolveOpusPayloadType();
                    nextDueMs = clock.ElapsedMilliseconds;
                    priming = true;
                    primingSinceMs = nextDueMs;
                    primingMaxWaitMs = PrimeMaxWaitMs;
                }

                // Wait for this slot: sleep to just short of the deadline, spin the rest.
                var remaining = nextDueMs - clock.ElapsedMilliseconds;
                if (remaining > 2)
                {
                    Thread.Sleep((int)(remaining - 1));
                }

                while (clock.ElapsedMilliseconds < nextDueMs)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    Thread.SpinWait(200);
                }

                var nowMs = clock.ElapsedMilliseconds;
                var lateMs = nowMs - nextDueMs;

                // A stall (the thread was not scheduled for a while). A short one is paid back one extra frame per
                // slot, which the browser's jitter buffer absorbs. A long one is written off: the slots that went by
                // are given up, timestamps included, so the stream stays on time instead of running late from then
                // on. Nothing queued is lost; it plays after a gap the length of the stall.
                var framesThisSlot = 1;
                if (lateMs >= MaxStallDebtMs)
                {
                    var skipped = lateMs / FrameDurationMs;
                    timestamp += (uint)skipped * RtpUnitsPerFrame;
                    nextDueMs += skipped * FrameDurationMs;
                    Interlocked.Add(ref _stallSlotsSkipped, skipped);
                    markNext = true;
                }
                else if (lateMs >= 2 * FrameDurationMs)
                {
                    framesThisSlot = 2;
                }

                for (var i = 0; i < framesThisSlot; i++)
                {
                    nextDueMs += FrameDurationMs;
                    var slotTimestamp = timestamp;
                    timestamp += RtpUnitsPerFrame;

                    byte[] frame;
                    var isRealAudio = false;

                    if (!_outgoing.IsEmpty)
                    {
                        // Audio arriving after the queue ran dry is pre-buffered again before it is released,
                        // whether it starts a new reply or continues one the provider stalled on. Priming after a
                        // mid-reply stall makes that one gap a little longer, but it is one gap rather than a
                        // stutter of several while the provider catches up.
                        if (emptySinceMs >= 0 && !priming)
                        {
                            priming = true;
                            primingSinceMs = nowMs;
                            primingMaxWaitMs = nowMs - emptySinceMs > SpurtGapMs ? PrimeMaxWaitMs : ResumePrimeMaxWaitMs;
                        }

                        if (priming && Volatile.Read(ref _queuedFrames) < PrimeFrames && nowMs - primingSinceMs < primingMaxWaitMs)
                        {
                            frame = FillerFrame(nowMs, ref emptySinceMs, lastRealFrameMs, ref provisionalHoleFrames);
                        }
                        else
                        {
                            priming = false;
                            emptySinceMs = -1;
                            isRealAudio = _outgoing.TryDequeue(out frame!);
                            if (isRealAudio)
                            {
                                Interlocked.Decrement(ref _queuedFrames);

                                if (provisionalHoleFrames > 0)
                                {
                                    if (lastRealFrameMs >= 0 && nowMs - lastRealFrameMs < ReplyContinuityMs)
                                    {
                                        Interlocked.Add(ref _midReplySilenceFrames, provisionalHoleFrames);
                                    }

                                    provisionalHoleFrames = 0;
                                }

                                lastRealFrameMs = nowMs;
                            }
                            else
                            {
                                frame = _silenceFrame;
                            }
                        }
                    }
                    else
                    {
                        frame = FillerFrame(nowMs, ref emptySinceMs, lastRealFrameMs, ref provisionalHoleFrames);
                    }

                    if (frame is not { Length: > 0 })
                    {
                        // The slot goes by unsent; its timestamp is already accounted for.
                        markNext = true;

                        continue;
                    }

                    try
                    {
                        _pc.SendRtpRaw(SDPMediaTypesEnum.audio, frame, slotTimestamp, markNext ? 1 : 0, payloadType);
                        markNext = false;

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The realtime audio pacing thread stopped unexpectedly.");
        }
        finally
        {
            if (highResolution)
            {
                EndHighResolutionTimer();
            }
        }
    }

    // The payload type negotiated for the audio the browser receives: the one its offer gave Opus. Packets sent under
    // any other number are silently discarded by the browser.
    private int ResolveOpusPayloadType()
    {
        try
        {
            var format = _pc.AudioStream?.GetSendingFormat();

            return format is { ID: > 0 } ? format.Value.ID : OpusPayloadType;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read the negotiated Opus payload type; using the offered default.");

            return OpusPayloadType;
        }
    }

    // What goes out in a slot that has no real audio to send. Inside a reply the first few slots send nothing at
    // all, so the browser bridges them by extending the last waveform (Opus packet-loss concealment), which for a
    // short gap sounds far better than a hard cut to silence; after that, and between replies, comfort silence
    // keeps the stream alive. Either way the slot's timestamp advances, so what follows is never late.
    private byte[] FillerFrame(long nowMs, ref long emptySinceMs, long lastRealFrameMs, ref long provisionalHoleFrames)
    {
        if (emptySinceMs < 0)
        {
            emptySinceMs = nowMs;
        }

        var midReply = lastRealFrameMs >= 0 && nowMs - lastRealFrameMs < ReplyContinuityMs;
        if (!midReply)
        {
            return _silenceFrame;
        }

        // Diagnostics: a gap inside a reply is audible — but only if the reply then continues. Counted
        // provisionally here and confirmed when the next real frame arrives in time; a reply that simply ended
        // discards the count.
        provisionalHoleFrames++;

        return nowMs - emptySinceMs < ConcealedGapMs ? [] : _silenceFrame;
    }

    // Windows schedules Thread.Sleep on the system timer, which defaults to a ~15.6 ms grid — far too coarse for a
    // 20 ms cadence. winmm's timeBeginPeriod(1) raises it to 1 ms process-wide while a peer is alive. Harmless
    // elsewhere (never called), and a failure just means the coarser grid.
    private static bool TryBeginHighResolutionTimer()
    {
        try
        {
            return NativeMethods.timeBeginPeriod(1) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void EndHighResolutionTimer()
    {
        try
        {
            _ = NativeMethods.timeEndPeriod(1);
        }
        catch (Exception)
        {
            // Nothing to do.
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint uPeriod);

        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint uPeriod);
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
                        _decoder.Decode(null, _decodeBuffer, OpusDecodeRate / 1000 * FrameDurationMs, false);
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

        int decoded;
        try
        {
            decoded = _decoder.Decode(payload, _decodeBuffer, _decodeBuffer.Length, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to Opus-decode an incoming realtime audio packet.");
            return;
        }

        if (decoded <= 0)
        {
            return;
        }

        // 48 kHz -> 24 kHz for the pipeline (see OpusDecodeRate).
        var samples = _decimator.Process(_decodeBuffer.AsSpan(0, decoded), _decimated);

        var count = Interlocked.Increment(ref _rtpReceived);

        var bytes = new byte[samples * 2];
        short peak = 0;
        for (var i = 0; i < samples; i++)
        {
            var sample = _decimated[i];
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

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "WebRTC realtime: peer closing after {FramesSent} assistant frames sent, {InboundPackets} microphone packets received, {MidReplySilence} gap frame(s) inside replies (provider stalls), {StallSlots} pacing slot(s) given up (thread stalls); provider audio {SamplesReceived} samples in, {SamplesEncoded} encoded ({Unencoded} not yet framed), largest chunk {LargestChunkMs} ms.",
                Interlocked.Read(ref _framesSent), Interlocked.Read(ref _rtpReceived), Interlocked.Read(ref _midReplySilenceFrames), Interlocked.Read(ref _stallSlotsSkipped),
                Interlocked.Read(ref _samplesReceived), Interlocked.Read(ref _samplesEncoded),
                Interlocked.Read(ref _samplesReceived) - Interlocked.Read(ref _samplesEncoded), _largestChunkSamples * 1000 / SampleRate);
        }

        try
        {
            await _pacingCts.CancelAsync().ConfigureAwait(false);

            // The pacer checks the token at least every frame slot; give it a moment to unwind.
            if (_pacingThread.IsAlive && !_pacingThread.Join(TimeSpan.FromMilliseconds(500)))
            {
                _logger.LogDebug("The realtime audio pacing thread did not stop within 500 ms.");
            }
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
