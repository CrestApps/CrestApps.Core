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

## Providers without a speech-to-speech model

Most providers do not ship a speech-to-speech model. A **cascaded realtime deployment** serves the same
experience by chaining three deployments you already have — one that transcribes, one that reasons, and one
that speaks:

```
mic ──▶ realtime speech-to-text ──▶ chat (tools, data sources) ──▶ text-to-speech ──▶ speaker
```

Everything above the client is unchanged: a cascaded deployment produces the same realtime messages a native
provider does, so the orchestrator, the chat UI, transcripts, and turn persistence all behave identically.
The legs may come from different vendors — transcribe with one, reason with another, speak with a third.

Configure it by storing `CascadedRealtimeMetadata` on a deployment that also declares the `realtime` feature:

```csharp
var deployment = await deploymentManager.NewAsync("cascaded-voice", clientName);

deployment.Alter<CascadedRealtimeMetadata>(cascade =>
{
    cascade.SpeechToTextDeploymentName = "elevenlabs-scribe";
    cascade.ChatDeploymentName = "gpt-4o";
    cascade.TextToSpeechDeploymentName = "elevenlabs-tts";
});

deployment.Alter<AIDeploymentMetadata>(metadata =>
{
    metadata.Features = [AIDeploymentFeatureNames.Realtime];
});
```

What to expect from each leg:

- **Speech-to-text** must expose a realtime client that supports a transcription session. It hears the user
  continuously; a committed transcript is what ends the user's turn and starts the reply. It transcribes with
  its own deployment's model, not the one a native realtime session would name.
- **Chat** produces the reply. Tools, data sources, and the profile's system message are applied here, so a
  cascaded session keeps every capability a text profile has. Function invocation is applied automatically.
- **Text-to-speech** speaks the reply. The reply is spoken a sentence at a time so audio starts playing while
  the model is still writing, and the voice picker for the deployment lists this leg's voices.

  It must answer with **raw PCM** at the session's output sample rate — `audio/L16` or `audio/pcm`. Realtime
  audio is played as headerless samples, so a reply encoded as MP3 or wrapped in a WAV container would be
  played as noise. The session refuses anything else rather than emitting it, naming the format it was given.
  Providers are asked for `Pcm<rate>` (for example `Pcm24000`); a provider that cannot produce raw PCM cannot
  serve this leg.

Two differences from a native speech-to-speech model are worth planning for. Latency is higher, because a turn
passes through three services instead of one. And interruption is driven by transcription: when the user
speaks over the assistant, the first partial transcript cancels the in-flight reply and tells the client to
drop the audio it has buffered, so barge-in responds as fast as the transcriber reports speech.

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

Assistant audio is Opus-encoded in CELT-only mode (the transform half of Opus, the one it uses for music) at
96 kbps VBR, complexity 10, with inter-frame prediction disabled so every frame decodes on its own. The encoder's
defaults would land at ~16 kbps in the hybrid mode, which is telephone quality, and the hybrid mode's SILK layer
re-synthesises everything below 8 kHz with a quality that varies from phoneme to phoneme. Prediction is off
because the browser's decoder does not see one continuous stream: it sees pre-encoded comfort silence before each
reply and across gaps, and packet-loss concealment where a slot went unsent, and a frame delta-coded against a
state the decoder never saw comes out at the wrong level (measured on a recorded reply: an error as large as the
signal itself in the first frames after such a splice; with prediction off, ~15 dB below it). In-band FEC is off:
it exists only in the SILK modes, and asking for it with an expected loss rate is what forced the hybrid mode.

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

## Voice profiles: choosing tools

A voice agent is only as reliable as its tool list is short. Every tool a realtime session carries sits in the
model's context on **every turn** of the conversation — not once per request, as in chat — and a model picks
well from a handful of clearly distinct tools and poorly from a catalog. This is how every voice-agent platform
works, and OpenAI's own realtime guidance says the same: curate a small, explicit tool set per agent. Nobody ships a
catalog into a voice loop.

**Pick the tools on the profile.** A customer-service voice profile with `validate_customer`, `lookup_account`,
`update_customer_info` and the automatic knowledge base search is four to six tools. That is the common case,
and it already works — select them on the profile's Tools tab and nothing more is needed.

**Take tools from MCP connections individually.** An MCP server routinely exposes dozens of tools. Rather than
attaching the whole connection, untick *Use all tools* under it and choose the few this profile needs. A profile
saved before this existed — or with the switch left on — takes every tool the connection offers, exactly as
before. An empty selection keeps the connection for its prompts and resources but contributes no tools.

### What makes the tools reliable

Tool count is rarely what breaks a customer-service voice agent in practice. These are:

1. **Ordering enforced by the tool, not the prompt.** The model must not call `update_customer_info` before
   `validate_customer` has succeeded. Instructions alone will not guarantee it — models skip steps under
   conversational pressure. Have `validate_customer` record a verified flag in
   `AIInvocationScope.Current.Items`, and have every mutating tool read it and refuse, with a short spoken-friendly
   message, when it is absent. The scope lives for the whole session, so the flag does too.
2. **Confirm before writes.** "I'll update your address to 12 Oak Street — is that right?" Misheard digits are the
   dominant voice failure, and this is the standard defence. It belongs in the system prompt of any profile with
   mutating tools.
3. **Tool results written to be spoken.** The model reads results aloud. A tool that returns a JSON blob produces
   a robotic answer or a stumble; one that returns `"Account found: Jane Doe, premium plan, last payment March 3."`
   produces a sentence. Audit any tool a voice profile will use for output shaped for a chat window.
4. **Distinct names and descriptions.** With a small set this is the whole selection problem. `lookup_account`,
   `find_customer` and `get_customer_details` side by side is how the model picks the wrong one.

### The safety net

If a profile still resolves more tools than chat's own scoping threshold (`DefaultOrchestratorOptions.ScopingThreshold`,
default 30), the realtime orchestrator trims the set at session open — by token relevance to the profile's
instructions, the same lightweight scoring the chat orchestrator uses, capped at `InitialToolCount` (default 20)
plus any tool that must be kept (the knowledge base search, and anything another tool depends on). It logs a
**warning** naming the profile and the tools the model will not see:

```
warn: Realtime session for 'AIProfile' resolved 42 tool(s), above the 30 a session carries well.
      Scoped to 21 by relevance to the profile's instructions; the model will not see: [...].
      Curate the profile's tools — or select fewer tools from its MCP connections — so this cut is not needed.
```

Treat that line as a to-do, not a feature. Chat can re-scope on every request because it has the user's message;
a voice session cannot without deferring every reply, so this one decision has to hold for the whole conversation.
A curated profile never triggers it.

### For breadth: hand off, don't grow the list

When a business genuinely needs sixty tools across billing, shipping and returns, the answer is not a longer list.
It is a small triage agent that hands off — "transfer to billing" is itself a tool, and calling it swaps the
session's instructions and tools to the specialist's small set. Each specialist stays short and reliable. This is
how OpenAI's own realtime agent examples are structured; the framework does not implement handoffs yet, but every
piece above is designed so that it can.

## Knowledge base grounding

A text completion retrieves from the knowledge base **before** the model runs: the profile's data source is
searched with the user's message and the matching chunks are placed in the system message, so the model cannot
fail to see them. A realtime session has no such moment — it opens before anyone has spoken, so there is no
query to search with.

Retrieval therefore runs **once per spoken turn**. When a profile has a data source (or session documents)
attached and preemptive RAG is enabled site-wide, the session is opened with `turn_detection.create_response =
false`, and each turn goes:

```
user stops speaking → provider commits + transcribes the turn
                    → host searches the knowledge base with that transcript
                    → retrieved chunks are added as a system conversation item
                    → host sends response.create → the model answers from them
```

This runs the same `IPreemptiveRagHandler` pipeline the text path uses — data source, documents, memory — so
citations, `IsInScope` strictness, top-N and filters all behave identically, and a grounded answer carries its
`[doc:n]` references into the transcript like any other. Extend it by registering an `IPreemptiveRagHandler`, or
replace the whole policy with your own `IRealtimeTurnGrounding`.

Three consequences worth knowing:

- **A grounded turn is slower to start**, by one vector search. The provider no longer replies the instant it
  stops hearing the user; it waits for the transcript and the search. Sessions without a knowledge base are
  untouched and keep the provider's own immediate replies.
- **Retrieval uses the utterance verbatim.** The text path first rewrites the message into focused queries with a
  utility LLM call; that is skipped here, because it would add a second round-trip to every spoken turn and it
  earns its cost by resolving follow-ups against conversation history, which a per-turn realtime context does not
  carry.
- **The search tool is still advertised, and every tool stays available on every turn.** The answer's
  `response.create` carries no tool overrides, so the session's full toolset and `tool_choice: auto` apply exactly
  as they do without grounding. Retrieval gives the model the knowledge up front; the tool lets it go looking for
  more.

### Covering the wait

Silence between the user finishing and the assistant starting reads as a broken assistant long before it reads as
a thoughtful one. So retrieval races a timer: if the search returns before
`GroundingAcknowledgementDelayMs` (default 700 ms) the answer comes straight back with nothing in front of it. If
it does not, the assistant says one short line — "let me look that up" — while the search finishes.

That acknowledgement is requested **out of band** (`conversation: "none"`), with tools off and a small token cap.
It is spoken, but never added to the conversation, never streamed as an assistant turn, and never persisted: the
model does not later see itself having said it, and it does not appear in history. A fast index never pays for it;
only a slow one does.

### The muteness backstop

A grounded session speaks only when the server asks it to, which means a swallowed turn — one that produces
neither a transcript nor a transcription failure — would leave the assistant mute for the rest of the
conversation. Every path therefore ends in a response request, including the failure paths, and
`GroundingResponseWatchdogSeconds` (default 15) is the backstop for the paths that do not exist yet. An ungrounded
answer is a poor answer; a silent assistant is a broken product, so the watchdog always prefers the former. It
logs a warning when it fires — if you see that line, something upstream is dropping turns.

### Switches

| Setting | Default | Effect |
| --- | --- | --- |
| Admin → Settings → *Enable preemptive RAG* | on | Governs **both** text and voice. Off means the model decides when to search, everywhere. |
| `CrestApps:AI:RealtimeTransport:EnableKnowledgeGrounding` | `true` | Voice only. Off leaves text grounded and returns voice to the search tool, which starts speaking sooner and grounds less reliably. |
| `CrestApps:AI:RealtimeTransport:GroundingAcknowledgementDelayMs` | `700` | How long retrieval may run before the wait is covered aloud. `0` never speaks one. |
| `CrestApps:AI:RealtimeTransport:GroundingResponseWatchdogSeconds` | `15` | How long a committed turn may go unanswered before a reply is requested anyway. `0` disables the backstop. |

The knowledge base is only as reachable as the deployment that calls it. If the deployment behind the session —
for a cascade, its **chat** leg — does not declare the `toolCalling` feature, the tools resolved for the session
are stripped before the model sees them, and the orchestrator logs an error naming the deployment. Starting a
session with tools but without an ambient `AIInvocationScope` now throws rather than letting every tool call
return an error string the model would relay as "I don't have that information".

### The vector store has to be configured

Grounding is only as good as the index behind it. A data source resolves its content manager from **its index
profile's provider**, so the connection for that provider must be configured in the host. An unconfigured one
throws on every search, and the profile then answers as though its knowledge base were empty — which for an
`IsInScope` profile means a confident "that information is not available in the current data sources" on every
question, with nothing in the grounding log to suggest a misconfiguration.

Locally the Aspire AppHost supplies this: it provisions `pgvector/pgvector:pg16` and injects
`CrestApps__PostgreSQL__ConnectionString` into both sample hosts. Running a sample host **on its own does not**,
so a profile whose index profile is PostgreSQL retrieves nothing until `CrestApps:PostgreSQL:ConnectionString` is
set another way.

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

## Session limits

A realtime session holds an open (billed) provider connection whether or not anyone is talking, so two guard
rails bound what a single session can cost. Both are on by default and both are configurable.

**Idle timeout.** A session ends after `IdleTimeoutSeconds` in which neither the user nor the assistant said
anything (30 by default; `0` disables it), reporting `session_ended` with reason `idle`. The clock is reset by
both sides of the conversation — user speech, a response starting, each chunk of assistant audio, a response
finishing — so a spoken answer longer than the window never trips it; only real silence does. That is what makes
a window as short as 30 seconds safe: a forgotten tab is closed in half a minute rather than ten.

**Maximum duration.** A session ends after `MaxSessionDurationSeconds` however busy it has been (300 — five
minutes — by default; `0` disables it), reporting `session_ended` with reason `max_duration`. The idle timeout
only catches a session nobody is using; this is the backstop for one that is genuinely held open, including a
runaway page whose audio keeps the idle clock warm. It bounds how long one session runs, not how long someone may
talk: the client says the limit was reached and offers to start another immediately.

Both reasons are reported to the browser, which releases the microphone, explains what happened, and shows a
**Resume** button.

## User controls

A realtime session is audio-only, so the controller hides the host's message box, send button, and
speech-to-text microphone button while one is running, leaving **Start speaking** as the only way in. It hides
whatever the host hands it: pass `input`, `sendButton`, and `micButton` in `selectors` alongside
`realtimeButton`, and a host that renders its own realtime surface can hide them server-side too, which avoids
showing them for the instant before the page's script runs.

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

Static credentials are the wrong tool for a hosted TURN service. Cloudflare and Twilio issue credentials with a
fixed lifetime — often 24 hours — so pasting a generated username and password here means realtime voice stops
working the next day, and the failure is quiet: the relay is refused, ICE falls back, and callers behind a strict
NAT simply get no audio. Use the section below instead.

### TURN through Cloudflare Realtime

Cloudflare does not expose a shared secret, so credentials cannot be signed locally the way coturn's
`use-auth-secret` allows — they are issued by an API call. Register the provider and give it the TURN token:

```csharp
services.AddCloudflareRealtimeTurn();
```

```json
{
  "CrestApps": {
    "AI": {
      "RealtimeTransport": {
        "Cloudflare": {
          "TokenId": "<TURN Token ID>",
          "ApiToken": "<API token>",
          "TtlSeconds": 86400
        }
      }
    }
  }
}
```

Nothing here expires. The key and token are long-lived, and every credential a browser sees is minted on demand,
so there is no rotation step to forget. Credentials are reused until they are halfway through `TtlSeconds` and
then replaced, which means roughly two API calls per lifetime rather than one per connection, and a credential
handed to a browser is always valid for at least as long again — no call can outlive the credentials it started
with.

Cloudflare answers with a matrix rather than a list: STUN and TURN over UDP, TCP, and TLS, each on its standard
port and again on a port chosen to slip through a restrictive firewall. That is more URLs than a browser wants —
Chrome warns past five of them because it gathers against every one, and so does the peer on this side, which is
handed the same list. The provider keeps the first URL Cloudflare lists for each scheme and transport and drops
the rest, which leaves every route out of a network intact — direct, relayed over UDP, over TCP, and over TLS —
while removing only the spare ports for relays that are already reachable. The URLs kept are Cloudflare's own, in
Cloudflare's own order, and the credentials are minted for the whole matrix, so each one is authenticated by the
credentials it ships with.

`StunUrls` and `TurnUrls` are ignored while Cloudflare is configured, because credentials issued for one relay do
not authenticate against another. Narrowing applies only to what Cloudflare itself issued: servers a deployment
names by hand are never trimmed, and never mixed in.

Two behaviours are worth knowing. While `TokenId` and `ApiToken` are unset the provider does nothing and whatever
was configured above still applies, so registering it before the key exists is safe. And if Cloudflare cannot be
reached, the credentials already issued keep being served — they are good for about half their lifetime — rather
than dropping every caller to STUN only; the failure is logged and retried within 30 seconds.

Changing `TokenId` or `ApiToken` at runtime, from an administration screen or a rotating secret store, takes effect
on the next connection: the options are watched and the cached credentials dropped.

### Sourcing ICE servers from somewhere else

Both halves of a session — the list handed to the browser and the server-relay peer itself — resolve through
`IRealtimeIceServerProvider`, so they can never disagree about which relay to use or which credentials to present.
Implement it to reach any other TURN service:

```csharp
public sealed class TwilioIceServerProvider : IRealtimeIceServerProvider
{
    public async ValueTask<IReadOnlyList<WebRtcIceServer>> GetIceServersAsync(CancellationToken cancellationToken = default)
    {
        // Mint credentials however the service requires, then return them.
    }
}

services.AddSingleton<IRealtimeIceServerProvider, TwilioIceServerProvider>();
```

The method is asynchronous precisely so an implementation may fetch credentials over the network. One that needs
no I/O should return a completed `ValueTask`, which allocates nothing.

### Options reference

| Property | Purpose |
| --- | --- |
| `EnableWebRtc` | Whether WebRTC is offered to browsers. Defaults to `true`. Turn it off on hosts with no inbound UDP and no reachable TURN relay — otherwise every session waits out the connect timeout before falling back. |
| `TurnDetectionType` | `semantic_vad` (default) lets the model decide when the user has finished; `server_vad` ends the turn after a fixed silence. A deployment that rejects semantic detection is switched to server VAD automatically. |
| `TurnDetectionEagerness` | For `semantic_vad`: `low`, `medium`, `high` or `auto` (default). Lower waits longer for the user to continue. |
| `IdleTimeoutSeconds` | How long a session may go with neither side speaking before it ends. Defaults to `30`; `0` disables it. |
| `MaxSessionDurationSeconds` | The longest a single session may run, however busy. Defaults to `300`; `0` disables it. |
| `StunUrls` | STUN server URLs. Defaults to a public server when empty. |
| `TurnUrls` | TURN server URLs (`turn:`/`turns:`). Empty means no relay. |
| `TurnSecret` | coturn `use-auth-secret` shared secret; enables ephemeral credentials. |
| `TurnCredentialTtlSeconds` | Lifetime of a minted ephemeral credential (default 3600). |
| `TurnUsername` / `TurnCredential` | Static TURN credentials, used only when `TurnSecret` is unset. Not suitable for a hosted TURN service, whose credentials expire. |
| `Cloudflare:TokenId` / `Cloudflare:ApiToken` | Cloudflare Realtime TURN credentials, named as the dashboard shows them. When both are set, credentials are minted per lifetime and the STUN and TURN properties above are ignored. |
| `Cloudflare:TtlSeconds` | Lifetime requested for each Cloudflare credential (default 86400). Refreshed at half this. |

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

### Tracing knowledge grounding

To confirm grounding is working, raise `CrestApps.Core.AI.Services.DefaultRealtimeTurnGrounding` and
`CrestApps.Core.AI.Chat.Realtime` to `Debug`. One healthy grounded turn looks like this:

```
info: Realtime session abc123: knowledge grounding is active. Replies are requested by the server
      after retrieval (acknowledgement after 700 ms, watchdog 15s).
dbug: Realtime knowledge grounding availability: True (dataSource=True, documents=False, handlers=True).
dbug: Realtime retrieval starting for utterance (31 chars) across 1 handler(s): DataSourcePreemptiveRagHandler.
dbug: Realtime retrieval returned 2841 chars and 3 citation(s) in 214 ms.
dbug: Realtime session abc123: retrieval finished in 219 ms and added context to the conversation.
dbug: Realtime session abc123: answer requested 221 ms after the transcript arrived.
```

What each line tells you:

- **No "knowledge grounding is active" line** — the session is not grounded. The availability line says why:
  `dataSource=False, documents=False` means nothing is attached to the profile; the two "grounding is off" lines
  name the switch that disabled it.
- **"added nothing"** — retrieval ran and the index returned nothing relevant for that utterance. The model still
  answers, and the search tool remains available to it.
- **"found no relevant content" on _every_ turn** — check the index provider's own logger before concluding the
  corpus is thin. A store that cannot be reached fails inside its content manager, which logs the failure under
  its own name (`PostgreSQLDataSourceContentManager`, and its peers) and hands back an empty result. Grounding
  cannot tell that apart from a genuine miss, so it reports an ordinary empty turn while every knowledge question
  goes unanswered. `PostgreSQL is not configured. A connection string is required.` is the usual culprit; see
  [The vector store has to be configured](#the-vector-store-has-to-be-configured).
- **"still running after 700 ms, so the wait is covered"** (Information) — the index is slower than the
  acknowledgement deadline. This is the line that explains why some turns say "let me look that up" and others
  do not.
- **"a committed turn produced no transcript within 15s"** (Warning) — the backstop fired. The session recovered,
  but something upstream dropped a turn and it will keep happening.
- **"does not declare the 'toolCalling' feature"** (Error) — the deployment that would call the tools, or a
  cascade's **chat** leg, has them stripped before the model sees them. Fix the model's capability metadata.
- **"resolved N tool(s), above the 30 a session carries well"** (Warning) — the profile brings more tools than a
  voice session should carry, and the set was trimmed at session open. The line lists what the model will not see;
  curate the profile or select fewer tools from its MCP connections.
- **"MCP connection '…': 3 of 40 tool(s) selected for this profile"** (Debug, `CrestApps.Core.AI.Mcp`) — per-tool
  selection is in effect for that connection.

The orchestrator's own debug line reports the tool count and whether per-turn retrieval is on for the session.

In the browser, `CoreAIRealtime`'s controller exposes `getState()` and `getGateLevel()` — the latter returns the
gate's most recent measurement (level, tracked noise floor, whether it is open, whether the assistant is audible),
which is the quickest way to tell "the mic is muted" apart from "the model is not responding".
