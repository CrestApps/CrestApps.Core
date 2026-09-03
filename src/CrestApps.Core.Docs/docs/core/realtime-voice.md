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
- Audio crosses the boundary as **PCM16 @ 24 kHz mono**. [SIPSorcery](https://www.nuget.org/packages/SIPSorcery) handles ICE/DTLS/SRTP/RTP and [Concentus](https://www.nuget.org/packages/Concentus) handles Opus — the codec reconciles the browser's native 48 kHz internally, so no hand-written resampling is involved.

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
thresholds but the same relative one. While the assistant is audible the margin is raised so its echo cannot open
the gate on its own, and "the assistant is speaking" carries a short hangover so the gate does not re-open in
every gap between its words. The gated signal is delayed ~80 ms so the gate is already open when the first
consonant arrives — without that, every utterance lost its opening sound, which is exactly what degrades
transcription.

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

These are per-device preferences (saved in the browser), independent of the transport:

- **Allow interruptions (barge-in)** — *on* (default) keeps the mic open so the user can talk over the assistant (relies on AEC on WebRTC). *Off* mutes the mic while the assistant speaks (half-duplex), a guaranteed no-echo mode for hostile audio setups or when the user prefers not to interrupt.
- **Push-to-talk** — hold <kbd>Space</kbd> (or a button) to open the mic; best for very noisy rooms.
- **Assistant volume** — lowers playback; on WebRTC this also reduces the echo the canceller must handle.
- **Echo guard delay** — the half-duplex hangover tail; only relevant when barge-in is off.
- **Voice gate** — *Auto* (default) sends only the user's speech, adapting to the room. *Off* keeps the microphone always open and relies on echo cancellation and the provider's voice-activity detection alone, which is the right choice with a headset. *Strict* raises the margins for open speakers in a loud room.
- **Audio setup** — *Headset*, *Laptop speakers*, or *Room speakers + separate mic*, each of which sets barge-in, the voice gate and automatic gain together. A setup is suggested from the device labels once microphone permission is granted (a headset has no acoustic path, so full duplex is safe; a standalone microphone in front of speakers does).
- **Test my audio** — plays a short two-tone chirp through the same output the assistant uses and measures how much of it survives echo cancellation on the way back in. More than ~6 dB above the room means the model will hear itself often enough to answer itself, and the room preset (half duplex) is chosen. This is the same decision meeting apps make, and it replaces guesswork for open-office users.
- **Microphone** and **Speaker**. Automatic speaker routing follows the preference above; the picker exists because Windows keeps a separate *Default Device* and *Default Communications Device*, so the sink that gives the best echo cancellation is not always the one the user is listening to. On Firefox the browser's own picker is used, since it exposes no output list until the user chooses.
- **Noise suppression**, **automatic gain**, **language**, and **turn-detection** tuning. Leaving the language on *Auto* sends no language hint at all, so the transcriber detects it — a bilingual user is not pinned to their browser's locale.

Barge-in and the turn-detection values apply to a conversation already in progress: they are enforced by the
browser's gate, the server's input pump and the provider's turn detection at once, and changing only one of them
leaves the three disagreeing. Turn-detection tuning currently reaches the provider on the Azure transport; on
OpenAI-direct deployments the values are applied to the browser and server halves only.

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
