---
sidebar_label: Realtime Voice
sidebar_position: 30
title: Realtime Voice (Speech-to-Speech)
description: How CrestApps.Core runs realtime speech-to-speech voice chat over WebRTC with automatic WebSocket fallback, acoustic echo cancellation, and TURN configuration.
---

# Realtime Voice (Speech-to-Speech)

> A realtime-capable AI profile can hold a live, spoken conversation — audio in, audio out — while still honoring everything a text profile gives you: system message, tools, data sources, and turn persistence. The audio is carried over **WebRTC** when available, falling back to **WebSocket** automatically.

## Overview

When a profile's chat mode is **Realtime** and its deployment is a realtime (speech-to-speech) model, the chat UI switches from text input to a live voice session. The browser captures the microphone, streams it to the server, and plays the assistant's spoken reply back — continuously, so the user can interrupt (barge-in) mid-sentence.

Two transports carry that audio. The application selects between them automatically; **there is no user-facing transport switch**:

| Transport | When it is used | Echo handling |
| --- | --- | --- |
| **WebRTC** (server-relay) | Primary — whenever the server advertises it and the browser supports `RTCPeerConnection`, and the peer connects | The browser's acoustic echo canceller (AEC) keeps the mic open (full-duplex) |
| **WebSocket** (PCM over SignalR) | Fallback — when WebRTC can't connect (blocked UDP, no TURN, unsupported browser) | Browser AEC still applies to the played-back audio; barge-in off additionally mutes the mic while the assistant speaks |

## Architecture: server-relay WebRTC

The realtime orchestrator, tool loop, system-prompt injection, and persistence are **transport-agnostic**. WebRTC swaps only the two audio boundaries; the server still drives the provider session.

```
Browser ──WebRTC/Opus──▶ Hub (SIPSorcery peer) ─decode→PCM16(24k)─▶ Runner ──WS/PCM──▶ Provider
Browser ◀─WebRTC/Opus── Hub (WebRTC sink)     ◀─encode←PCM16(24k)── Runner ◀───────────── (same runner)
```

- The browser peers with **the application's hub**, not the model provider. The hub keeps talking to the provider over the existing WebSocket, so WebRTC works with any realtime provider without provider-side WebRTC.
- **Transcripts, errors, and speech events stay on SignalR.** The WebRTC path carries audio only; the SignalR connection is also used for WebRTC signaling (SDP offer/answer and ICE candidates).
- Audio crosses the boundary as **PCM16 @ 24 kHz mono**. [SIPSorcery](https://www.nuget.org/packages/SIPSorcery) handles ICE/DTLS/SRTP/RTP and [Concentus](https://www.nuget.org/packages/Concentus) handles Opus. Assistant audio is encoded at 24 kHz, which browsers decode correctly at their native 48 kHz. Microphone audio is decoded at 48 kHz and downsampled to 24 kHz by the peer itself: asking Concentus to decode straight to 24 kHz attenuates the output by roughly 40 dB (a full-scale voice arrived at about −37 dBFS, below the provider's speech detection), which is what made sessions sit at *Listening* without ever answering.

## Enabling the WebRTC transport

The transport lives in the `CrestApps.Core.AI.Realtime.WebRtc` package. Register it during startup:

```csharp
builder.Services.AddWebRtcRealtimeTransport();
```

When this service is registered, the realtime hubs offer WebRTC as the primary transport and the chat views advertise the capability to the client. If it is **not** registered, realtime voice still works — it simply uses the WebSocket transport everywhere. This is an application/deployment decision, not a user setting.

Both realtime hosts support it:

- **Chat Interactions** — `ChatInteractionHub`.
- **AI Chat** — `AIChatHubCore` (profile chat and the embedded chat widget).

## Transport selection and fallback

Selection happens once, at connect time — audio is never migrated mid-session:

1. If WebRTC is advertised and the browser supports it, the client requests the mic, asks the hub for the ICE servers (`GetRealtimeIceServers`), creates an `RTCPeerConnection` with them, sends its SDP offer to the hub, and applies the answer.
2. If the peer reaches a connected state, the realtime session starts over WebRTC.
3. If the peer does not connect within ~8 seconds, or the connection/ICE state fails, the client tears the attempt down and restarts on the WebSocket transport.

The ICE servers are fetched per session rather than embedded in the page, so ephemeral TURN credentials are always fresh — a page left open longer than the credential lifetime would otherwise hand the browser a dead credential.

A post-connect drop simply ends the session; it does not attempt to migrate to WebSocket.

## Session lifecycle events

The server owns the session's lifecycle and reports it to the browser over a single client method,
`ReceiveRealtimeEvent(identifier, type, payload)`:

| Event | Meaning |
|---|---|
| `session_ready` | The provider session is open; the client moves from *connecting* to *listening*. |
| `speech_started` | The provider heard the user. With barge-in on, the client stops playback immediately. |
| `playback_flush` | Buffered assistant audio has been superseded and must be dropped. |
| `user_turn_pending` | An utterance was captured and is being transcribed; the client shows a placeholder in the right place. |
| `user_turn_dropped` | That utterance produced nothing worth showing; the client removes its placeholder. |
| `session_ended` | The session is over (`completed`, `cancelled`, `idle`, or `error`); the client releases the microphone. |

This matters most on the WebSocket transport, where the provider streams a reply faster than real time and
several seconds of it are already scheduled in the browser's Web Audio graph: without a server-driven flush the
interrupted reply plays to the end and the new one is appended behind it. `session_ended` is what stops a
browser streaming audio into a session the server has already torn down.

## Acoustic echo cancellation (open rooms)

The reason to prefer WebRTC is **acoustic echo cancellation**. In an open room (external speakers + an open mic, e.g. a webcam mic on a monitor) the assistant's voice travels through the air back into the microphone. Without cancellation, the model hears — and answers — itself.

To get reliable AEC, the client:

- Requests `echoCancellation: true` on the microphone.
- Renders the assistant's audio into an `<audio>` element routed via `setSinkId(...)` to the **communications sink** (Chromium) or, failing that, the concrete device the `default` alias points at. Rendering there is what couples playback with the microphone's echo canceller as a proper reference. Browsers that expose neither alias — Firefox lists only concrete outputs — are left on their own default, because guessing a device there routed the assistant to whatever happened to be enumerated first (often an HDMI monitor) and it appeared silent.

Both Chrome and Firefox include browser-rendered audio in the echo canceller's reference, so the WebSocket path
is cancellable too; what WebRTC adds is the communications-mode coupling on Windows/Chromium and the jitter
buffer's stable timing.

If the operating system's default output device is not the speakers the user actually hears, playback is routed there — so the default playback device should be the user's real speakers.

## The microphone gate

Echo cancellation alone still leaves residual echo that the provider's voice-activity detection can mistake for
speech (which is how a model ends up answering itself, and how a transcriber invents phantom "Thank you" turns).
So the outbound microphone is gated: it is silent unless the user is genuinely speaking.

The gate runs in an **AudioWorklet** on the audio thread, which matters for correctness as much as for quality —
`requestAnimationFrame` is paused for hidden documents, so a gate driven from it freezes the moment the user
switches tabs, and a gate frozen closed means they are never heard again.

It decides in dBFS against a **noise floor it tracks continuously** (falling fast, rising slowly), rather than
against a fixed level: a quiet webcam microphone and a loud conference room need very different absolute
thresholds but the same relative one. The gated signal is delayed ~80 ms so the gate is already open when the
first consonant arrives — without that, every utterance lost its opening sound, which is exactly what degrades
transcription.

There are no user-facing gate modes or thresholds; everything the gate needs it measures for itself. The part
that makes open speakers work is the **echo return level**: while the assistant is audible and the gate is shut,
the gate learns how loud the assistant's own voice comes back into the microphone (relative to the assistant's
level) after echo cancellation has done what it can. From then on, while the assistant is audible, only a voice
clearly louder than that expected echo — sustained for a quarter of a second — counts as the user interrupting.
With a headset the expected echo is the room floor and interruptions are cheap; with loud speakers on the desk it
is high and the gate simply waits its turn, which is the honest behaviour for that room. The estimate warms up
within the first reply and follows slowly afterwards, so a user interrupting cannot be mistaken for echo in the
time it takes to confirm them, and turning the volume down is noticed within a couple of seconds. "The assistant
is speaking" carries a short hangover so the gate does not re-open in every gap between its words.

The same gate runs on both transports, so the WebSocket fallback is not the one path on which the model can hear
its own echo.

## Playback quality

Assistant audio is Opus-encoded at 64 kbps VBR (complexity 10, in-band FEC on); the encoder's defaults would land
at ~16 kbps, which is telephone quality.

The audio itself is played exactly as the provider produced it: every sample that arrives is framed, encoded and
sent in order, and nothing on the way to the browser resamples, stretches or trims it. What the browser's jitter
buffer reacts to is packet *timing*. A packet that arrives late relative to its RTP timestamp makes the buffer
add delay and then speed speech up to shrink that delay again, which users hear as words rushing for a moment.
The peer therefore paces frames from a dedicated above-normal-priority thread (a timer callback on Windows fires
on a ~16 ms grid and runs on a thread pool that builds and test runs in the same process starve), asks Windows
for 1 ms timer resolution while a peer is alive, and sends exactly one frame per 20 ms slot. The RTP timestamp is
driven by the wall clock rather than by what was sent: every slot owns a frame's worth of timestamp whether a
frame went out in it or not, so a frame is never late against its own timestamp and the browser never has to
catch up. A slot with nothing to send — the provider stalled mid-reply, or the pacing thread was not scheduled —
is left to the browser's packet-loss concealment for up to 80 ms and then filled with comfort silence: a short gap
the browser forgets, not a delay the rest of the reply carries. Because the provider delivers audio in bursts of
up to a second with pauses between them, a reply is buffered ~300 ms (400 ms at most) before it is released and
briefly again (200 ms at most) when it resumes after a stall. The client, where the browser supports
`RTCRtpReceiver.jitterBufferTarget`, pins the receiver's cushion at 150 ms so ordinary network jitter lands inside
it instead of making the buffer re-adapt.

Two measurements exist for this. The server logs, when a peer closes, how many gap frames landed inside replies
(provider stalls), how many pacing slots were given up (thread stalls), how many provider samples came in versus
were encoded, and the largest provider chunk seen. In the browser,
`CoreAIRealtime.activeController.getTransportStats()` returns the receiver's own counters — packets lost, jitter,
jitter-buffer delay, concealment, samples inserted or removed by time-stretching — which is the first thing to
read if playback ever sounds choppy; the live end-to-end check logs them and can also record what the browser
plays (`REALTIME_E2E_RECORD=1`). A 35-second reply measured 0 packets lost, 0 ms jitter, 0 concealment events and
0 accelerated samples in both Chrome and Firefox, with the jitter buffer sitting at its 150 ms target.

## Turn detection

The provider decides when the user has finished speaking. By default the session asks for **semantic** turn
detection (`turn_detection.type = semantic_vad`): the model judges whether the utterance is complete, so a pause
for thought in the middle of a question does not make the assistant answer the first half of it. This is the single
biggest difference between a conversation that feels natural and one that talks over the user. If a deployment
rejects semantic detection, the runner switches the session to plain server VAD (a silence timer) in place and the
conversation continues.

Both the algorithm and the semantic *eagerness* are configurable under `CrestApps:AI:RealtimeTransport`
(`TurnDetectionType`, `TurnDetectionEagerness`). The gate above stays open for two seconds after the user's level
drops, comfortably longer than any pause the detector is willing to wait through, because the gate emits digital
silence when it closes and whichever of the two expires first is what actually ends the turn.

## Turn bookkeeping

Two things about realtime turns are not obvious and shape how the transcript is built.

**Input-audio transcription lags the spoken reply.** The model answers the audio before the transcriber has
finished with it, so a user turn created when its text arrives is stamped *after* the assistant's answer and
history reloads with the prompt underneath its own reply. The turn is therefore created when the provider commits
the utterance — `user_turn_pending` puts a placeholder in the conversation at that moment — and its text is filled
in later, keeping the original timestamp.

**Utterances and transcripts are paired by the provider's item id, never by arrival order.** Transcription can
fail outright, and with barge-in off some utterances are never answered at all; either one shifts an
order-based pairing by one turn, which silently removes an answered prompt from the conversation.

With barge-in off, whether an utterance gets answered is decided when the provider *commits* it, not when speech
starts — so an utterance that began over the assistant but committed after it finished is answered normally.

## Interruptions

When the user barges in, the server tells the provider how much of the reply was actually heard
(`conversation.item.truncate`), so the rest is removed from the model's context. Without it the model believes it
delivered the whole answer, and follow-ups like "what did you just say?" reflect text the user never heard.

How much was heard is known exactly on WebRTC, where the peer reports the audio it still has queued. On the
WebSocket transport the server only knows what it sent, so the truncation is an over-estimate that trims nothing —
no worse than not truncating at all.

## Idle sessions

A realtime session holds an open (billed) provider connection whether or not anyone is talking. Sessions end
after `IdleTimeoutMinutes` without user speech (10 by default; set it to `0` to disable), reporting
`session_ended` with reason `idle`. The client says so and offers to resume.

## User controls

The settings popover is deliberately short. A user should be able to press **Start speaking** and talk, in any
room, without first understanding acoustics; everything that used to be a knob (echo margins, gate modes,
turn-detection timing, audio-setup presets, an echo self-test) is measured or decided automatically now. What
remains are per-device preferences (saved in the browser), independent of the transport:

- **Microphone** and **Speaker**. Automatic speaker routing follows the preference above; the picker exists because Windows keeps a separate *Default Device* and *Default Communications Device*, so the sink that gives the best echo cancellation is not always the one the user is listening to. On Firefox the browser's own picker is used, since it exposes no output list until the user chooses.
- **Assistant volume** — lowers playback; this also reduces the echo the canceller and the gate must handle.
- **Language** — *Automatic* sends no language hint at all, so the transcriber detects it and a bilingual user is not pinned to their browser's locale. Choosing a language pins both transcription and the assistant's replies to it.
- **Allow interruptions** — *on* (default): talk over the assistant to interrupt it. The gate's learned echo level is what keeps the assistant's own voice from counting as an interruption, so this is safe with a headset, laptop speakers and most desk speakers alike. Turn it *off* only if the assistant keeps hearing itself: the microphone is then muted while it speaks (half duplex).
- **Push-to-talk** — hold <kbd>Space</kbd> (or the button) to open the mic; for very noisy places.

Noise suppression and automatic gain are always on. Interruptions apply to a conversation already in progress:
they are enforced by the browser's gate, the server's input pump and the provider's turn detection at once, and
changing only one of them leaves the three disagreeing, so a change is sent to all three.

## Configuration: STUN and TURN

ICE (NAT traversal) servers are configured under `CrestApps:AI:RealtimeTransport` and bound to `RealtimeTransportOptions`. STUN enables direct connectivity through most home/office NATs. A **TURN** server is required for users behind strict/symmetric NATs or blocked UDP, where traffic must be relayed — without it, those users fall back to WebSocket.

### STUN only (default)

With no configuration, a public STUN server is used so direct connections work out of the box:

```json
{
  "CrestApps": {
    "AI": {
      "RealtimeTransport": {
        "StunUrls": [ "stun:stun.l.google.com:19302" ]
      }
    }
  }
}
```

### TURN with ephemeral credentials (recommended for production)

Stand up [coturn](https://github.com/coturn/coturn) in `use-auth-secret` mode with a shared secret. The server then mints **short-lived ephemeral credentials per session**, so a long-lived TURN password never reaches the browser:

```json
{
  "CrestApps": {
    "AI": {
      "RealtimeTransport": {
        "StunUrls": [ "stun:turn.example.com:3478" ],
        "TurnUrls": [ "turn:turn.example.com:3478", "turns:turn.example.com:5349" ],
        "TurnSecret": "the-same-secret-configured-in-coturn",
        "TurnCredentialTtlSeconds": 3600
      }
    }
  }
}
```

The credential is derived exactly as coturn's TURN REST API expects: the username is a UNIX expiry timestamp and the credential is `Base64(HMAC-SHA1(secret, username))`. Configure coturn with the matching secret, for example:

```ini
use-auth-secret
static-auth-secret=the-same-secret-configured-in-coturn
realm=turn.example.com
```

### TURN with static credentials

For simpler setups you can use a long-lived username and password instead of a secret (less secure — prefer the ephemeral secret in production):

```json
{
  "CrestApps": {
    "AI": {
      "RealtimeTransport": {
        "TurnUrls": [ "turn:turn.example.com:3478" ],
        "TurnUsername": "turn-user",
        "TurnCredential": "turn-password"
      }
    }
  }
}
```

If `TurnUrls` is set but neither a secret nor static credentials are provided, no TURN entry is offered (there would be nothing to authenticate with).

### Options reference

| Property | Purpose |
| --- | --- |
| `EnableWebRtc` | Whether WebRTC is offered to browsers. Defaults to `true`. Turn it off on hosts with no inbound UDP and no reachable TURN relay — otherwise every session waits out the connect timeout before falling back. |
| `TurnDetectionType` | `semantic_vad` (default) lets the model decide when the user has finished; `server_vad` ends the turn after a fixed silence. A deployment that rejects semantic detection is switched to server VAD automatically. |
| `TurnDetectionEagerness` | For `semantic_vad`: `low`, `medium`, `high` or `auto` (default). Lower waits longer for the user to continue. |
| `IdleTimeoutMinutes` | How long a session may go without user speech before it ends. Defaults to `10`; `0` disables it. |
| `StunUrls` | STUN server URLs. Defaults to a public server when empty. |
| `TurnUrls` | TURN server URLs (`turn:`/`turns:`). Empty means no relay. |
| `TurnSecret` | coturn `use-auth-secret` shared secret; enables ephemeral credentials. |
| `TurnCredentialTtlSeconds` | Lifetime of a minted ephemeral credential (default 3600). |
| `TurnUsername` / `TurnCredential` | Static TURN credentials, used only when `TurnSecret` is unset. |

## Verifying which transport a session used

## Testing

The browser half has its own suites, because the behaviour that matters — whether the microphone gate opens,
whether playback stops, whether the microphone is released — cannot be reached from a server-side test:

```bash
npm run test:gate
```

Runs the gate's decision rules in Node against the same pure function the AudioWorklet uses. No browser, no audio,
no timing: every threshold is asserted as a number.

```bash
npx playwright install chromium firefox
npm run test:client
```

Runs the client against a static harness page in Chromium and Firefox with fake media devices — no server, no
SignalR connection and no AI provider are involved. It covers transport selection and fallback, the session state
machine, and that the gate actually gates a live audio graph.

```bash
REALTIME_E2E_PROFILE_ID=<realtime profile id> REALTIME_E2E_PASSWORD=<password> \
REALTIME_E2E_WAV=<16-bit PCM WAV of a spoken question> \
npx playwright test --config tests/realtime-client/e2e/playwright.e2e.config.js --project=chromium
npx playwright test --config tests/realtime-client/e2e/playwright.e2e.config.js --project=firefox
```

A live end-to-end check against a running host and a real realtime deployment. The WAV becomes the microphone —
Chromium plays it through its fake capture device; Firefox has no file-backed fake microphone, so the test
replaces `getUserMedia` with a Web Audio graph playing the same file — and the test expects the spoken question to
appear as the user's transcript and a reply to follow. This exercises the gate, the WebRTC transport (including
SIPSorcery's interop with each browser), the provider's turn detection and the transcript pipeline together, and
is the quickest way to confirm a deployment actually converses. `REALTIME_E2E_BARGE_IN=false` runs it with
interruptions off. On Windows a suitable WAV can be produced with the built-in speech synthesizer
(`System.Speech.Synthesis.SpeechSynthesizer`, 48 kHz, 16-bit, mono, with a couple of seconds of leading silence).
The test logs the gate's live measurements, and the server logs the inbound peak amplitude every five seconds
under `CrestApps.Core.AI.Realtime.WebRtc` — between them a silent failure is explainable.

## Diagnostics

On the server, the WebRTC peer logs its lifecycle (connection/ICE state, first inbound packet decoded, first
assistant frame sent) under the `CrestApps.Core.AI.Realtime.WebRtc` logger, and warns when the inbound microphone
buffer overflows. The runner logs response start/completion and session end reasons under
`CrestApps.Core.AI.Chat.Realtime.RealtimeChatSessionRunner` — including a warning when a session ran a whole
conversation without the provider ever reporting user speech, which means that deployment's events are not
recognised and barge-in cannot work for it.

In the browser, `CoreAIRealtime`'s controller exposes `getState()` and `getGateLevel()` — the latter returns the
gate's most recent measurement (level, tracked noise floor, whether it is open, whether the assistant is audible),
which is the quickest way to tell "the mic is muted" apart from "the model is not responding".
