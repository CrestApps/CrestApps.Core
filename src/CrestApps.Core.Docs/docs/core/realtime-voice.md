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
| **WebSocket** (PCM over SignalR) | Fallback — when WebRTC can't connect (blocked UDP, no TURN, unsupported browser) | Half-duplex echo guard mutes the mic while the assistant speaks |

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

1. If WebRTC is advertised and the browser supports it, the client requests the mic, creates an `RTCPeerConnection`, sends its SDP offer to the hub, and applies the answer.
2. If the peer reaches a connected state, the realtime session starts over WebRTC.
3. If the peer does not connect within ~8 seconds, or the connection/ICE state fails, the client tears the attempt down and restarts on the WebSocket transport.

A post-connect drop simply ends the session; it does not attempt to migrate to WebSocket.

## Acoustic echo cancellation (open rooms)

The reason to prefer WebRTC is **acoustic echo cancellation**. In an open room (external speakers + an open mic, e.g. a webcam mic on a monitor) the assistant's voice travels through the air back into the microphone. Without cancellation, the model hears — and answers — itself.

To get reliable AEC, the client:

- Requests `echoCancellation: true` on the microphone.
- Renders the assistant's audio into an `<audio>` element whose output is routed to the **concrete default output device** via `setSinkId(...)`. Rendering to the concrete communications/default device is what couples playback with the microphone's echo canceller as a proper reference. (This mirrors how major browser voice apps route their playback.)
- With barge-in **on**, it does not pull the assistant's remote stream into a Web Audio graph, which would disturb that reference.

If the operating system's default output device is not the speakers the user actually hears, playback is routed there — so the default playback device should be the user's real speakers.

## User controls

These are per-device preferences (saved in the browser), independent of the transport:

- **Allow interruptions (barge-in)** — *on* (default) keeps the mic open so the user can talk over the assistant (relies on AEC on WebRTC). *Off* mutes the mic while the assistant speaks (half-duplex), a guaranteed no-echo mode for hostile audio setups or when the user prefers not to interrupt.
- **Push-to-talk** — hold <kbd>Space</kbd> (or a button) to open the mic; best for very noisy rooms.
- **Assistant volume** — lowers playback; on WebRTC this also reduces the echo the canceller must handle.
- **Echo guard delay** — the half-duplex hangover tail; only relevant when barge-in is off.
- **Microphone**, **noise suppression**, **automatic gain**, **language**, and **turn-detection** tuning.

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
| `StunUrls` | STUN server URLs. Defaults to a public server when empty. |
| `TurnUrls` | TURN server URLs (`turn:`/`turns:`). Empty means no relay. |
| `TurnSecret` | coturn `use-auth-secret` shared secret; enables ephemeral credentials. |
| `TurnCredentialTtlSeconds` | Lifetime of a minted ephemeral credential (default 3600). |
| `TurnUsername` / `TurnCredential` | Static TURN credentials, used only when `TurnSecret` is unset. |

## Verifying which transport a session used

During development, the realtime settings panel is transport-aware: on a WebRTC session the **Echo guard delay** slider is hidden (it only applies to the half-duplex WebSocket path). On the server, the WebRTC peer logs its lifecycle (connection/ICE state, first inbound packet decoded, first assistant frame sent) under the `CrestApps.Core.AI.Realtime.WebRtc` logger.
