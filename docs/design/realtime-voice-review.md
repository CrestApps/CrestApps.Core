# Realtime Voice (Speech-to-Speech) — Implementation Review, Findings, and Remediation Plan

**Branch:** `ma/realtime-orcestration`
**Date:** 2026-09-03
**Status:** Implemented — see §0 for what shipped, what deviated, and the one item left open.
**Goal:** Understand the realtime client end to end (WebRTC primary, WebSocket fallback, barge-in on/off),
identify bugs and design flaws that stand between the current behavior and a ChatGPT-grade experience in
Chrome and Firefox, across headset and open-office (standalone mic + loud speakers) setups, and give a
concrete, prioritized plan for each item.

---

## 0. Implementation progress (2026-09-03)

**Status:** All 30 numbered findings are implemented, along with the §4 environment presets and echo self-test
and the P3 polish list. One item is deliberately left partial — see *Still open* below.

Verification: 2936 xUnit tests, 33 browser tests (Chromium + Firefox), 14 Node tests — all green. Assets and the
docs site build. What has **not** been verified is anything needing real hardware or a live provider; see
*Verification* at the end.

Where the implementation differs from what this document proposed, the reason is recorded.

### Done

| Finding | What shipped | Notes / deviations |
|---|---|---|
| **F1** ICE servers never reach the browser | `GetRealtimeIceServers()` on both hubs returning `RealtimeIceServerModel` with `urls`/`username`/`credential` pinned via `JsonPropertyName`. Both clients `invoke` it immediately before `new RTCPeerConnection`, falling back to the public STUN default. | Property names pinned explicitly rather than trusting the hub protocol's naming policy — WebRTC requires `urls` verbatim. The host-supplied `webRtcIceServers` config is kept as an override. |
| **F2** long invocations block the client | `MaximumParallelInvocationsPerClient = 8` in `ConfigureCrestAppsChatHubOptions`. | Took option (a). The structural fix (b) is still worth doing, but (a) removes the user-visible failure with far less risk. |
| **F3** WS barge-in doesn't stop playback | The SignalR sink sends `speech_started` / `playback_flush`; both clients flush scheduled Web Audio. | As proposed. |
| **F4** no end-of-session signal | `IRealtimeConversationSink` gains `SessionReadyAsync`/`SessionEndedAsync`; the runner emits ready after `orchestrator.StartAsync` and *always* emits ended in a `finally`. Clients stop on `session_ended`, on `ReceiveError` during a session, and on `onclose`/`onreconnecting`. | Put in the **runner** rather than each hub's `try/finally`: it is transport-agnostic and already knows why the session ended, so it cannot drift between the two hubs. |
| **F5** title generation on the audio pump | `GenerateRealtimeSessionTitleAsync` yields immediately and runs off the pump; the hub awaits it once at session end so the title is still saved. | As proposed, plus the end-of-session await so a title is never lost. |
| **F6** gate freezes in background tabs | The gate is an `AudioWorkletProcessor` registered from a Blob URL, with a `setInterval` + `getFloatTimeDomainData` fallback. | As proposed. |
| **F7** fixed gate thresholds | RMS in dBFS, adaptive noise floor (fast down / slow up), open at floor + 9 dB (12 strict), floor + 12/18 dB plus an absolute −40 dBFS floor while the assistant is audible, hysteresis on close, 80 ms look-ahead delay. New **Voice gate** setting: Auto / Off / Strict. | As proposed. The decision is now one pure function (`CoreAIRealtime.decideGate`) stringified into the worklet and reused by the fallback, with 14 Node tests over its rules. |
| **F8** no assistant-speaking hangover | "Assistant is speaking" holds 400 ms past the last audible block; listen-grace starts only when that expires. | Client-side hangover; the server-driven variant is covered by F35. |
| **F9** ignored-utterance FIFO desync | `UserTurnCommitted` (from `input_audio_buffer.committed`) and `UserTranscriptFailed` are mapped; utterances and transcripts are paired on the provider's `item_id`, and "will it be answered?" is judged at **commit** rather than at speech-started. | Both causes fixed. The late-commit case — an utterance that began during a reply but committed after it — is now answered and shown, where it used to be thrown away. |
| **F10** failed responses merge into the next bubble | The runner flushes the assistant turn on **every** `ResponseCompleted` with content, and surfaces `response.status_details.error`. | As proposed. |
| **F11** OpenAI-direct: no speech-started | `Map` falls back to the raw representation's type name when it is an SDK object rather than a `JsonElement`, for both speech-started and commit. A session that never sees speech-started now logs a warning naming the consequence. | **Deviation:** turn-detection overrides still reach the provider only on the Azure transport. Building the OpenAI SDK's options object cannot be verified without a live OpenAI-direct deployment, so the gap is reported at runtime and documented rather than written blind. |
| **F12** transcript ordering | The user turn is created at commit with an empty body and a `user_turn_pending` event, then filled in when transcription completes (`UpdateUserTurnAsync`), keeping the earlier timestamp. All three hosts render the placeholder. | As proposed. `IRealtimeTurnStore` gains update/delete; the stores track recent turns in a small capped map. |
| **F13** barge-in doesn't truncate the item | `IRealtimeConversation.TruncateAssistantAudioAsync` sends `conversation.item.truncate` with the audio the user actually heard, tracked per item from the audio handed to the sink minus what the transport still holds, minus the browser's jitter buffer. | Exact on WebRTC (the peer reports `QueuedPlaybackMs`). On WebSocket the server only knows what it sent, so it over-estimates and trims nothing — no worse than not truncating. |
| **F14** pacing drift and RTP gaps | The pacing loop releases frames against elapsed wall-clock time (bounded catch-up) and fills gaps with a pre-encoded comfort-silence frame. | **Deviation:** the proposal was to advance the RTP timestamp across a gap, but SIPSorcery 10's `MediaStreamTrack.Timestamp` is read-only. Keeping the stream running achieves the same contiguity through the public API. The silence frame is encoded in the constructor because the Opus encoder is also used from the provider pump thread. |
| **F15** unbounded inbound buffer, 50 msgs/s | Inbound channel bounded at ~2 s with `DropOldest` (warning when full); the runner batches microphone audio to ~100 ms before `SendAudioAsync`. | As proposed, minus "discard until `session_ready`" — the bounded channel already caps what can accumulate. |
| **F16** wrong output device on Firefox | `setSinkId` only when a `communications`/`default` alias exists, plus a **Speaker** picker in both hosts (Chromium enumeration; `selectAudioOutput()` on Firefox) that applies live. | As proposed, including the picker. |
| **F17** fallback and connect UX | Client states with a status line, `sessionStorage` memory of a failed WebRTC attempt, a shortened wait once ICE gathering finishes without a relay candidate, a compatibility-mode notice, and `RealtimeTransportOptions.EnableWebRtc`. | As proposed. |
| **F18** two copies of the client | The microphone gate, output routing, autoplay handling, preset table, device-based preset suggestion and echo self-test are one implementation on `window.CoreAIRealtime`, used by both clients. `realtime-audio.js` now loads on every AI Chat host. | **Partial** — see *Still open*. |
| **F19** "Auto" pins the browser locale | Auto sends `null`, so transcription auto-detects and no language directive is added. | Server already handled a null language; this was purely client-side. |
| **F20** settings changed mid-session diverge | `UpdateRealtimeSettings` on both hubs → `RealtimeSessionControl` applies the change to the input pump and re-sends `session.update`. Both clients push changes made during a session. | Reaches the provider on the Azure transport; see F11. |
| **F21** push-to-talk swallows typing | Space ignored when the event target is an `input`, `textarea`, `select`, or `contenteditable`. | As proposed. |
| **F22** no conversation state feedback | `idle / requesting-mic / connecting / listening / ended / playback-blocked`, an `onStateChange` callback, `getState()`, `getGateLevel()`, and a status line with `aria-live` rendered by the module itself. | The mic level meter is available via `getGateLevel()` but no host draws one. |
| **F23** upload stream buffer stalls | `StreamBufferCapacity = 64` on the chat hubs. | As proposed. |
| **F24** documentation mismatches | `realtime-voice.md` corrected and extended: lifecycle events, the gate, turn bookkeeping, interruptions, idle sessions, presets and the echo test, per-session ICE, the new options, and how to run the tests. | As proposed. |
| **F25** test gaps | 33 Playwright tests (Chromium + Firefox) against a static harness with fake media, plus 14 Node tests over the gate's decision rules. `npm run test:gate`, `npm run test:client`. | The suite found two real bugs while being written: a shadowed identifier that threw on every `session_ended`, and a settings popover that rendered off-screen. |
| **F26** provider session limits and idle sessions | `RealtimeTransportOptions.IdleTimeoutMinutes` (default 10); the runner ends the session with reason `idle`, and both clients say so and offer **Resume**. | As proposed. |
| **F27** provider closes swallowed | An abnormal WebSocket close yields an error event carrying the close status and description, logged at Warning and surfaced to the user. | **Deviation:** done by emitting an error event rather than threading an `ILogger` through the client factory — same two goals, no plumbing change. |
| **F28** communications sink may be the wrong device | The Speaker picker from F16, plus help text in both panels explaining the Windows *Default Device* / *Default Communications Device* split. | As proposed. |
| **F29** autoplay policy | Explicit `play()` and `AudioContext.resume()`, with rejection surfaced as `playback-blocked` and a visible status line. | As proposed. |
| **F35** response "over" before it is heard | `IRealtimeConversationSink.PendingPlaybackMs` (from the peer's `QueuedPlaybackMs`); with barge-in off the runner holds the half-duplex gate until the queued audio drains, superseded by any newer response. | Implemented as a runner-side delay keyed on a response generation counter rather than a `playback_drained` callback. |
| **§4** presets and echo self-test | Three presets, label-based suggestion, and a chirp-based echo test that measures residual against the room floor and picks half duplex above ~6 dB. | As proposed. |
| **P3** polish | Localizable strings via `strings` on `attach`; `DOMPurify` guarded with a text fallback; `WebRtcRealtimePeerRegistry.Add` returns and the hubs dispose a displaced peer; ring buffer for the outgoing accumulator; Opus track declared mono; Opus packet-loss concealment on a sequence gap. | The panel also now clamps itself horizontally, which the browser tests caught. |

### Still open

- **F18, the last step.** `ai-chat.js` still runs its own transport setup, settings panel and push-to-talk UI
  rather than calling `CoreAIRealtime.attach`. Everything a user experiences is at parity and the substantial
  logic is shared, so what remains is duplicated wiring. It is left undone deliberately: `ai-chat.js` reuses its
  conversation button and mode flags for realtime, so the migration rewires the AI Chat page and the embedded
  widget, and the browser suite covers `realtime-audio.js` rather than `ai-chat.js` in its host page — testing it
  properly needs a host with a configured realtime deployment. Doing it blind is how the widget breaks silently.
  The seam to build first is a settings-panel component that owns the preferences and reports changes, instead of
  reaching into the host's variables as `attach`'s panel does today.
- **iOS Safari** remains out of scope, as this document had it.

### Verification

- `dotnet test tests/CrestApps.Core.Tests -c Release` — 2936 passed, 0 failed. Ten new realtime runner tests
  cover: session ready/ended, transcription-failed alignment, an utterance committing after the response
  finished, turn ordering, truncation on barge-in, failed-response flush, drain-aware half-duplex, mid-session
  settings, and the idle timeout.
- `npm run test:client` — 33 passed in Chromium and Firefox (1 skipped: the blocked-main-thread observation,
  which headless Firefox cannot support because it stalls its own audio graph alongside the main thread).
- `npm run test:gate` — 14 passed.
- `npm run build` and the docs site build clean; the Blazor and MVC hosts build with no warnings.
- **Not verified**: anything needing real hardware or a live provider — the manual matrix in §7, TURN through a
  blocked-UDP network, SIPSorcery interop with Firefox, truncation against a real provider, the OpenAI-direct
  path, and how the pacing and comfort-silence changes actually sound. Those still need the manual passes below.

### Second review (2026-09-03, after two users tested the build above)

Two testers reported: (1) headset user — interruptions were *off by default*, and with them switched on in Chrome
the session showed *Listening* but never answered; (2) open-office user (webcam microphone, two desk speakers,
Chrome) — the assistant answered after the first few words instead of waiting for the whole question, and then
would not take another prompt for a while. The server log for (2) shows 20 continuous seconds of audio at about
−10 dBFS reaching the provider while the assistant was replying: louder than the user's own speech, i.e. the
speakers' echo passing the level-only gate. Both testers also found the settings panel far too complicated.

What was found and changed:

| Cause | Fix |
|---|---|
| **The WebRTC transport delivered the microphone to the provider about 40 dB too quiet.** `SipSorceryWebRtcRealtimePeer` asked Concentus to decode the browser's 48 kHz Opus straight to 24 kHz; Concentus 2.2's decode-side resampler attenuates that output by ~40 dB (measured: a −6 dBFS tone decodes to peak 102/32767 at 24 kHz and 16522 at 48 kHz). A full-scale voice therefore reached the provider at about −37 dBFS — below its speech detection — which is the real reason "Listening…" never answered a headset user, and why the open-office user's session only reacted when the echo of two desk speakers was loud enough. Found with the new live end-to-end check (fake microphone playing a spoken WAV): the gate opened on the speech, an in-page WebRTC loopback received it at full level, yet the server logged −37 dBFS peaks. | Decode at 48 kHz (exact) and downsample 2:1 in the peer with a 31-tap low-pass FIR (`Pcm48To24Decimator`). Verified: decode48 + decimate returns a −6 dBFS tone at −6 dBFS with aliases rejected. The 24 kHz *encoder* direction was fine, which is why the assistant was always audible. |
| The removed device-label guess had stored the *room* preset (interruptions **off**) for any microphone whose name matched a pattern, and the version-2 preference repair deliberately left `bargeIn` alone. That is why interruptions were "off by default". | Preferences version 3 restores interruptions and drops every removed field. |
| The gate judged an interruption by level alone (`floor + margin`, absolute −40 dBFS). With desk speakers the echo residual after cancellation was louder than the user, so the assistant's own reply opened the gate, the provider heard it, interrupted itself, and kept detecting "speech" for as long as the reply played — the "won't take another prompt" symptom. | The gate now learns the **echo return level** (how loud the assistant comes back into the microphone relative to its own level) while the assistant is audible and the gate is shut, and only opens for a voice clearly above the *expected echo*, sustained for 250 ms. Warms up within the first reply; falls when the volume is turned down. With loud speakers the gate waits its turn; with a headset interruptions stay cheap. Covered by `gate-decision.test.js` (loud-speaker echo over a 20 s reply, first-reply echo before the estimate settles, real interruption over echo, headset, volume change). |
| Turn detection was a silence timer (`server_vad`, 800 ms), so a pause for thought mid-question ended the turn and the assistant answered half of it. | Sessions request **`semantic_vad`** by default (`RealtimeTransportOptions.TurnDetectionType` / `TurnDetectionEagerness`); the model decides when the user is done. If a deployment rejects it, the runner switches the live session to server VAD and the conversation continues. The gate hangover is now 2 s so the gate never ends a turn before the detector would. |
| `UpdateTurnDetectionAsync` re-sent the whole session configuration, including the voice. The provider rejects a voice once the assistant has spoken, and that error was surfaced and ended the session — so toggling interruptions mid-conversation could kill it. | It now sends a partial `session.update` carrying only `turn_detection`. |
| A barge-in truncation naming more audio than the item held produced a provider error that was shown to the user. | Treated as benign (logged). |
| The WebSocket fallback sent the raw microphone: it was the one transport where the model could hear its own echo. | The same gate now runs on the WebSocket path, with the assistant's Web Audio playback tapped as the gate's reference. |
| The settings panel exposed audio-setup presets, an echo test, gate modes, an echo-guard delay, turn-detection sliders, and noise/gain switches. | The panel is now: microphone, speaker, volume, language, **Allow interruptions** (default on), push-to-talk. Everything else is measured or decided automatically. |
| `ai-chat.js` still carried its own ~1,100-line copy of the realtime client (the last open item from F18), so every fix had to be made twice and the two hosts had already diverged. | The AI Chat host now calls `CoreAIRealtime.attach` like the chat-interaction host; the copy is gone. |

Tests: `gate-decision.test.js` rewritten for the new rules (22 tests, including the cold-start cases: a user
already talking when the microphone goes live, and a loud room heard first); new C# tests for the semantic
default, the tuned-silence-implies-server-VAD rule, the semantic/server wire format, and the runner's fallback on
a rejected turn-detection configuration.

**Verified live** (`tests/realtime-client/e2e`, a real MVC host and the Azure `gpt-realtime` deployment, a
synthesized spoken question as the microphone):

| Browser | Interruptions | Result |
|---|---|---|
| Chromium (fake capture device) | on | "Hello there." → greeting; the greeting is cut off by the continuing question (barge-in, `response.done` status `cancelled`); "What is the capital of France?" → "The capital of France is Paris." Server inbound peak 32767/32767 (was 486 before the decoder fix). |
| Chromium | off | "Hello there." → reply; the recorded user keeps talking over the reply and is correctly not heard (half duplex); the fragment after the reply gets its own answer. |
| Firefox (getUserMedia replaced by a Web Audio graph playing the WAV) | on | Full exchange, transcript in order, "The capital of France is Paris." — first confirmation of SIPSorcery interop with Firefox. |

Full C# suite green after the changes; `npm run test:gate` 22/22; `npm run test:client` green in Chromium and
Firefox.

Not verified live: the OpenAI-direct provider, TURN through a blocked-UDP network, and real loudspeaker echo
(the fake microphone has no acoustic path, so the echo-return learning is covered by the gate unit tests only).
The open-office tester's setup is the one to re-test first.

---

## 1. How it works today

### 1.1 Components

| Layer | File | Role |
|---|---|---|
| Shared browser controller | `src/Resources/CrestApps.AI.Resources/Assets/js/realtime-audio.js` (`window.CoreAIRealtime`) | Mic capture, WebRTC/WS transport selection + fallback, duck-and-detect mic gate, playback, push-to-talk, per-device settings popover. Used by **Chat Interactions** (MVC `Chat.cshtml`, Blazor via `chat-interaction.js`). |
| AI Chat controller (duplicate) | `Assets/js/ai-chat.js` lines ~2105–3000 | A second, hand-maintained copy of the same logic for **AI Chat sessions** and the embedded widget. |
| Hub methods | `AIChatHubCore.StartRealtimeConversation / StartRealtimeWebRtc / AddRealtimeIceCandidate`; `ChatInteractionHubBase` (same three) | Authorization, capability gate, session creation, peer creation, run the session for the lifetime of the hub invocation. |
| Runner | `CrestApps.Core.AI.Chat/Realtime/RealtimeChatSessionRunner.cs` | Transport-agnostic pump: mic audio → provider; provider events → sink + persistence; half-duplex enforcement when barge-in is off. |
| Sinks | `SignalRRealtimeConversationSink` (private, one per hub) and `WebRtcRealtimeConversationSink` | Deliver audio (SignalR base64 PCM, or Opus over the peer) and transcripts/errors (always SignalR). |
| WebRTC peer | `CrestApps.Core.AI.Realtime.WebRtc/SipSorceryWebRtcRealtimePeer.cs` | SIPSorcery ICE/DTLS/SRTP; Concentus Opus 24 kHz; 20 ms pacing loop for outbound audio; flush on barge-in. |
| ICE config | `RealtimeWebRtcIceServers.cs`, `RealtimeTransportOptions.cs` | STUN/TURN (ephemeral coturn creds) — **server side only today (see F1).** |
| Orchestrator | `CrestApps.Core.AI/Realtime/DefaultRealtimeOrchestrator.cs`, `DefaultRealtimeSessionConfigurator.cs`, `DefaultRealtimeConversation.cs` | PREPARE pipeline reuse, tools via MEAI `UseFunctionInvocation`, provider events → neutral `RealtimeConversationEvent`. |
| Azure transport | `CrestApps.Core.AI.OpenAI.Azure/Realtime/*` | Raw WebSocket to Azure OpenAI GA realtime; JSON protocol mapping. OpenAI-direct uses MEAI's `OpenAIRealtimeClient`. |

### 1.2 Session lifecycle (WebRTC primary)

1. User clicks **Start speaking** → `getUserMedia({echoCancellation:true, noiseSuppression, autoGainControl})`.
2. `ensureConnected()` → `isRealtimeActive = true` (button flips to *End Conversation* immediately).
3. `RTCPeerConnection({iceServers})` — iceServers is **always the hard-coded Google STUN** (F1).
4. Mic → Web Audio gate (`setupWebRtcMicGate`) → gated track added to the peer. A hidden `<audio autoplay>` is created, routed via `setSinkId('communications' | default-group device | first output)` (F16/F28).
5. Offer sent via `StartRealtimeWebRtc(...)`; server creates a SIPSorcery peer, returns the answer via `ReceiveRealtimeAnswer`, trickles its candidates via `ReceiveRealtimeIceCandidate`. Browser candidates go up via `AddRealtimeIceCandidate` — **queued behind the long-running hub invocation (F2)**.
6. Server waits ≤ 20 s for ICE connected, then `RealtimeChatSessionRunner.RunAsync` (orchestrator PREPARE, tools, provider WebSocket, `session.update`).
7. Client waits ≤ 8 s for `connected`; on timeout / `failed` it tears the peer down and restarts on the WebSocket path (`fallbackToWebSocket`).
8. Audio: browser Opus → server decodes to PCM16 24 kHz → `input_audio_buffer.append` per 20 ms packet. Assistant PCM → Opus 20 ms frames → 20 ms `PeriodicTimer` pacing → RTP.
9. Transcripts/errors → SignalR (`ReceiveConversationUserMessage`, `ReceiveConversationAssistantToken/Complete`, `ReceiveError`).
10. End: user clicks End → client closes peer/stops tracks; server sees `Closed` → cancels the runner. **The server never tells the client that a session ended (F4).**

### 1.3 Barge-in ON (default)

| Layer | Behavior |
|---|---|
| Provider | `turn_detection.interrupt_response = true`, `create_response = true`, `silence_duration_ms = 800` (default), threshold optional. |
| Server runner | Forwards all audio. On `speech_started`: persists the partial assistant turn, `sink.SpeechStartedAsync` → WebRTC peer flushes its pacing queue. **WS sink: no-op (F3).** |
| Client (WebRTC) | Gate opens when mic peak > 0.05 (+0.08 while the assistant is audible), 1 s hangover; digital silence otherwise. Relies on browser AEC for the echo. |
| Client (WS) | Mic always open, no gate; AEC only. Scheduled playback is **never flushed on interruption (F3)**. |

### 1.4 Barge-in OFF (half-duplex)

| Layer | Behavior |
|---|---|
| Provider | `interrupt_response = false`. VAD still emits `speech_started`; a commit during an active response yields the benign "active response in progress" error. |
| Server runner | Input pump **drops** mic audio while `ResponseActivity.Active` (set on `response.created`, cleared on `response.done` — i.e. before playback finishes, F35). Utterances that began during a response are recorded in a FIFO so their lagging transcript is dropped (F9). On `ResponseStarted` the sink flushes stale playback (WebRTC only). |
| Client (WebRTC) | Gate open only when the assistant is *not* audible and (user active or within 1.5 s listen-grace after the assistant stops). The "assistant audible" test has no hangover (F8). |
| Client (WS) | Mic muted (silence frames) while `currentTime < playHead + hangover`. |

### 1.5 AEC strategy

- The mic is opened with `echoCancellation: true` on both transports (the WS path additionally asks Chromium-only hints `echoCancellationType:'system'`, `voiceIsolation`).
- On WebRTC the assistant is rendered through an `<audio>` element routed to the *communications* sink (Chrome/Edge on Windows) or the default output's concrete device. The memory note from earlier work confirms this is what made the open-room echo go away in Chrome.
- Both Chrome and Firefox include all browser-rendered audio in the AEC reference (Chrome via the audio service render mixer, Firefox via cubeb duplex streams), so Web Audio playback on the WS path is also cancelled in principle. The practical difference is the *communications-mode* coupling on Windows/Chrome and the WebRTC jitter buffer's stable timing. The docs overstate this (F24).
- The client-side gate exists because AEC alone left residual echo that the provider's VAD treated as speech (self-answering, Whisper "Thank you" hallucinations). ChatGPT does **not** gate; it relies on AEC + server VAD. The gate is the biggest source of fragility in the current design (F6, F7, F8).

---

## 2. Findings

Severity: **P0** = breaks the feature or the promised behavior for a real class of users; **P1** = clearly wrong UX/behavior that users will hit; **P2** = robustness / quality; **P3** = polish.

Each finding has: symptom → root cause (file) → how to fix → how to verify.

### P0 — must fix

#### F1. Configured STUN/TURN servers never reach the browser
- **Symptom:** `RealtimeTransportOptions` (TURN with ephemeral coturn credentials) is documented as the way to support strict NATs, but every browser peer is created with the hard-coded `stun:stun.l.google.com:19302`. Users behind symmetric NAT / blocked UDP always fall back to WebSocket after an 8 s wait, regardless of TURN configuration.
- **Root cause:** `realtime-audio.js:55` and `ai-chat.js:715` read `webRtcIceServers` / `config.realtimeWebRtcIceServers`, but no host supplies it: MVC `ChatInteraction/Chat.cshtml` (attach call ~L2436), Blazor `ChatInteractions/Chat.razor` config JSON (~L735), AI Chat pages/widget (no `data-coreai-chat-realtime-webrtc-ice-servers` attribute; `ai-chat.js:3921` only maps `realtimeWebRtcEnabled`). `RealtimeWebRtcIceServers.Resolve` is only used for the SIPSorcery peer.
- **Fix:**
  1. Add a hub method to both hubs: `Task<IReadOnlyList<WebRtcIceServer>> GetRealtimeIceServers()` returning `RealtimeWebRtcIceServers.Resolve(services)` (camelCase JSON: `urls`, `username`, `credential`). Calling it right before `new RTCPeerConnection` guarantees fresh ephemeral TURN credentials (TTL) instead of page-render-time ones.
  2. In `realtime-audio.js` `startRealtimeWebRtcConversation`, `await connection.invoke('GetRealtimeIceServers')` (fallback to the STUN default on failure), then build the peer.
  3. Remove the dead `webRtcIceServers`/`realtimeWebRtcIceServers` plumbing or keep it as an override.
  4. Mark the hub method `[Authorize]`-equivalent as the other methods (it leaks TURN credentials only to authenticated callers).
- **Verify:** unit test on the hub method; Playwright test asserting `RTCPeerConnection.getConfiguration().iceServers` contains the configured `turn:` URL; manual test from a network with UDP blocked + coturn → session must connect over relay (`chrome://webrtc-internals` shows `relay` candidate pair).

#### F2. Long-running hub invocations block every other hub call from the same client
- **Symptom:** During a voice session, browser ICE candidates (`AddRealtimeIceCandidate`), the WebSocket fallback (`StartRealtimeConversation`), `ClearHistory`, settings updates, `LoadSession`, `SendMessage`, etc. are not processed until the session ends. Today ICE only succeeds because SIPSorcery learns the browser's address as a *peer-reflexive* candidate from the browser's connectivity checks — fine on LAN, fragile through NAT/TURN. The fallback after a WebRTC timeout can stall up to 20 s more (the server's `connected.Task.WaitAsync(20 s)` still holds the slot).
- **Root cause:** SignalR's `HubOptions.MaximumParallelInvocationsPerClient` defaults to **1** (not configured anywhere: `CrestApps.Core.SignalR/ServiceCollectionExtensions.cs`, `AI.Chat/ServiceCollectionExtensions.cs:53`). `StartRealtimeWebRtc` and `StartRealtimeConversation` (`AIChatHubCore.cs:962/1108`, `ChatInteractionHubBase.cs:724/787`) `await runner.RunAsync(...)` for the whole session inside the invocation.
- **Fix (pick one; (a) is the quick, safe one):**
  - (a) In `AddCoreAIChatHub` set `options.MaximumParallelInvocationsPerClient = 8` (or expose via options). Keep the StoreCommitter behavior in mind: parallel invocations each get their own DI scope/store session, which is already how the STT conversation path behaves.
  - (b) Structural: make the realtime hub methods return after the handshake and run the session on a connection-scoped background task (`WebRtcRealtimePeerRegistry` already keys per connection; add a `RealtimeSessionRegistry` holding the CTS + task; cancel in `OnDisconnectedAsync` and in an explicit `StopRealtimeConversation` hub method). Then persistence commits must happen inside the task (the runner's hooks already do that).
- **Verify:** integration test with an in-memory SignalR test server: start a realtime session with a fake orchestrator that never completes, then invoke another hub method and assert it returns within 1 s. Manually: confirm `AddRealtimeIceCandidate` log lines appear *during* the session, not at teardown.

#### F3. WebSocket transport + barge-in ON: interrupting does not stop playback
- **Symptom:** The provider streams audio faster than real time, so several seconds of the reply are already scheduled in Web Audio. When the user interrupts, the old audio keeps playing to the end and the new reply is appended *after* it. This makes barge-in on the fallback transport feel broken.
- **Root cause:** `SignalRRealtimeConversationSink.SpeechStartedAsync` / `FlushPlaybackAsync` are no-ops in both hubs (`AIChatHubCore.cs:2726–2738`, `ChatInteractionHubBase.cs:2149–2161`) with a comment claiming the client handles it; the client only calls `flushRealtimePlayback()` from `stopRealtimeConversation` (`realtime-audio.js:999`, `ai-chat.js:2311`).
- **Fix:**
  1. Add one generic client method to `IAIChatHubClient` and `IChatInteractionHubClient`: `Task ReceiveRealtimeEvent(string identifier, string type, string? payloadJson)` with types `session_ready`, `speech_started`, `response_started`, `response_completed`, `playback_flush`, `session_ended`. (One method keeps interface churn low and gives the client the state machine it needs for F4/F12/F22.)
  2. `SignalRRealtimeConversationSink.SpeechStartedAsync` → `ReceiveRealtimeEvent(id, "speech_started")`; `FlushPlaybackAsync` → `"playback_flush"`.
  3. Client: on `speech_started` (barge-in on) and `playback_flush` call `flushRealtimePlayback()`; also mark the current assistant bubble as interrupted.
- **Verify:** unit test on the sink (RecordingSink already exists — add a SignalR client mock); Playwright: long reply, interrupt at 1 s, assert `AudioContext` scheduled sources are stopped (expose a debug hook) and the next reply starts within ~500 ms.

#### F4. No end-of-session signal; errors and reconnects leave a zombie client session
- **Symptom:** When the server-side session ends for any reason (provider WebSocket closed, 60-minute session cap, orchestrator exception, capability/authorization error, `ReceiveError`, SignalR reconnect), the client keeps the mic open, the button stays on *End Conversation*, and audio is streamed into a dead subject / dead peer. On the AI Chat host `ReceiveError` only stops STT recording (`ai-chat.js:1163`), never realtime; the shared module has no `ReceiveError`, `onclose`, or `onreconnected` handling at all; the MVC interaction page has no `onclose` handler.
- **Root cause:** `RealtimeChatSessionRunner.RunAsync` returns silently; hub methods have no `finally` that notifies the caller; the client has no server-driven "ended" transition.
- **Fix:**
  1. Server: wrap the runner call in both hub methods with `try/finally { await caller.ReceiveRealtimeEvent(id, "session_ended", reason) }` (reasons: `completed`, `provider_closed`, `error`, `peer_closed`, `timeout`). Send `session_ready` once the provider session is open (after `orchestrator.StartAsync`) so the client can show *Listening*.
  2. Client (shared module): accept `connection` lifecycle: register `connection.on('ReceiveRealtimeEvent')`, `connection.on('ReceiveError')` (stop + surface while active), `connection.onclose/onreconnecting` (stop, show "Voice session ended — reconnecting…"), `onreconnected` (offer *Resume*). Expose `onStateChange`.
  3. AI Chat host: same via the shared module once F18 is done; until then mirror the handlers in `ai-chat.js`.
- **Verify:** kill the provider socket (fake provider that ends the stream) → client must transition to idle within 1 s and show the reason; drop the SignalR connection (devtools offline) → same.

#### F5. Session title generation runs inline in the audio pump (AI Chat host)
- **Symptom:** On the first turn the assistant's audio stutters/gaps for 1–3 s.
- **Root cause:** `AIChatHubCore.cs:1056–1064` (`OnUserUtteranceAsync` → `GenerateSessionTitleAsync`, an LLM call) is awaited inside `RealtimeChatSessionRunner.PumpOutputAsync` (`PersistUserTurnAsync`), which is the same loop that forwards `AssistantAudioDelta`. The user transcript typically arrives *while* the reply is streaming.
- **Fix:** run the utterance hook off the pump: `_ = Task.Run(...)` with its own DI scope and error logging, or queue title generation and run it from `OnAssistantCompletedAsync` after the turn (still one LLM call, no audio impact). Keep DB writes on the pump (fast) but never network calls to a model.
- **Verify:** unit test: a slow `OnUserUtteranceAsync` (500 ms delay) must not delay the next `AssistantAudioAsync` sink call by more than a few ms.

#### F6. The mic gate stops working in background tabs
- **Symptom:** User switches tab or minimizes the window while talking; the gate freezes in its last state. If it was closed the user is never heard; if open, echo/noise leaks.
- **Root cause:** `setupWebRtcMicGate` drives the gate with `requestAnimationFrame` (`realtime-audio.js:800`, `ai-chat.js:2528`). Chrome and Firefox pause rAF for hidden documents. Additionally the analysis reads a 10.7 ms window (`fftSize=512` @48 kHz) ~60×/s, so ~35 % of the signal is never observed, and `getByteTimeDomainData` gives 8-bit resolution.
- **Fix:** move the gate into an `AudioWorkletProcessor` (runs on the audio thread regardless of visibility; per-128-frame decisions; can implement look-ahead, RMS, hysteresis natively). Register it from an inline Blob URL so no extra asset is needed. Fallback for browsers without AudioWorklet: `setInterval(20 ms)` + `getFloatTimeDomainData` with `fftSize = 2048`.
- **Verify:** Playwright: start a session, `page.evaluate(() => document.hidden)` via a second tab, feed fake speech, assert audio still reaches the server (peer inbound peak > 0 in the server log).

### P1 — clearly wrong behavior users will hit

#### F7. Fixed gate thresholds: quiet users can't be heard, loud rooms leak echo, onsets are clipped
- **Symptom:** With a webcam/monitor mic a normal voice may peak at 0.02–0.04 and never open the gate (the commit history mentions "the model never detects speech"); while the assistant speaks the bar rises to 0.13 so barge-in needs shouting. Conversely with AGC on in a loud room residual echo exceeds 0.13 and the model answers itself. The gate also opens only *after* speech is detected, so the first 20–50 ms of every utterance (initial consonant) is cut, which degrades Whisper transcripts and the model's understanding.
- **Root cause:** `REALTIME_GATE_OPEN_LEVEL = 0.05`, `REALTIME_GATE_ECHO_MARGIN = 0.08`, peak-based, no noise-floor tracking, no look-ahead (`realtime-audio.js:83`, `ai-chat.js:2526`).
- **Fix (inside the worklet from F6):**
  - Compute RMS in dBFS per 10 ms block; track an adaptive noise floor (fast-follow down, slow-follow up, e.g. 0.3 s / 3 s time constants). Open when level > floor + 9 dB (barge-in on) or > floor + 12 dB and > −40 dBFS while the assistant is audible; close after the 1 s hangover once below floor + 3 dB (hysteresis).
  - Add a `DelayNode` (~80 ms) on the *gated* path only (analyser reads the undelayed signal) so the gate opens before the onset reaches the peer. 80 ms extra latency is imperceptible in conversation.
  - Expose a *Voice gate* setting: `Auto` (default), `Off` (rely on AEC + server VAD, what ChatGPT does), `Strict` (higher margins for loud rooms).
- **Verify:** unit-test the gate decision function with synthetic frames (speech at −30 dBFS over −55 dBFS floor opens; echo residual at −38 dBFS while assistant audible does not; onset sample preserved via delay).

#### F8. "Assistant speaking" has no hangover → barge-in-off mic opens in every pause
- **Symptom:** With barge-in off, during the assistant's inter-word/inter-sentence pauses the gate opens (listen-grace re-arms on every dip), sending echo tail + room noise. The provider's VAD fires `speech_started`, the runner books an ignored utterance, and the provider emits the benign "active response in progress" error. This is the mechanism behind the previously observed split replies / phantom turns.
- **Root cause:** `assistantSpeaking = assistantLevel > 0.03` evaluated per frame; `if (wasAssistantSpeaking && !assistantSpeaking) listenGraceUntilMs = now + 1500` (`realtime-audio.js:823–833`).
- **Fix:** define `assistantSpeaking` as "level above threshold within the last 400 ms"; start listen-grace only when that hangover expires. Better still, drive it from the server: `response_started` / `playback_drained` events (F35) rather than measuring the remote level.
- **Verify:** unit test on the gate function with an assistant signal that has 250 ms gaps: gate must stay closed.

#### F9. Barge-in-off "ignored utterance" FIFO can desynchronize the transcript
- **Symptom:** An answered prompt disappears from the transcript, or an ignored prompt is shown as unanswered.
- **Root cause:** `RealtimeChatSessionRunner.cs:181–237` assumes one `UserTranscript` per `UserSpeechStarted`, and decides "ignored" at `speech_started` time. Both assumptions fail: (1) `conversation.item.input_audio_transcription.failed` is parsed by the Azure protocol but not mapped in `DefaultRealtimeConversation.Map`, so a failed transcription shifts the queue; (2) the provider decides whether to create a response at *commit* time (`speech_stopped`), so an utterance that started during a response but ended after `response.done` **is** answered — its transcript is then dropped.
- **Fix:** key everything on the provider's `item_id`: map `input_audio_buffer.committed` (carries `item_id`) → new event `UserTurnCommitted { ItemId }`; map `InputAudioTranscriptionCompleted/Failed` with `ItemId` (already parsed); map `response.created` → `ResponseStarted { ResponseId }` and use `conversation.item.created` (previous item) or the error `event_id` correlation to know whether the item was answered. Simplest robust rule: an utterance is "ignored" iff the provider sent the "active response in progress" error *for its commit*. Alternatively drop the server-side utterance-drop entirely and rely on the input-pump gate (which already prevents the audio from reaching the provider). Add a `(transcription unavailable)` placeholder on `Failed`.
- **Verify:** extend `RealtimeChatSessionRunnerTests` with: transcription-failed in the middle; utterance starting during a response but committing after `ResponseCompleted` (must be shown).

#### F10. Failed/incomplete responses merge into the next bubble
- **Symptom:** After a provider `response.done` with `status: failed|incomplete` (rate limit, content filter, max tokens) the accumulated text keeps the same `MessageId`, and the next reply's deltas append to it.
- **Root cause:** `RealtimeChatSessionRunner.cs:258–270` flushes only on `cancelled`.
- **Fix:** flush on every `ResponseCompleted` when `turn.HasContent`; map `response.status_details.error` into an `Error` event (non-benign) so the user sees why.
- **Verify:** runner test with `ResponseCompleted(status: "failed")` followed by a new response.

#### F11. OpenAI-direct provider: barge-in signal and turn-detection overrides silently don't work
- **Symptom:** With an OpenAI (non-Azure) realtime deployment, interruptions don't flush playback, partial assistant turns aren't persisted, and the 800 ms silence default / user VAD tuning are ignored.
- **Root cause:** `DefaultRealtimeConversation.Map` detects `speech_started` only when `RawRepresentation is JsonElement` (`DefaultRealtimeConversation.cs:107–117`) — true for the custom Azure transport only; MEAI's `OpenAIRealtimeClient` sets raw representations to OpenAI SDK objects (the DLL references `OpenAI.Realtime` types, not JSON). `RealtimeTurnDetectionOverrides` via `RawRepresentationFactory` is likewise only read by `AzureRealtimeProtocol`.
- **Fix:** (1) In `Map`, add a fallback: if `message.Type == RawContentOnly`, inspect `RawRepresentation.GetType().Name` (`InputAudioSpeechStartedUpdate`, `InputAudioSpeechFinishedUpdate`, …) or `JsonSerializer.SerializeToElement(raw)` and read `type`/`kind`. (2) For the OpenAI provider, apply the overrides by building the SDK `ConversationSessionOptions` in `RawRepresentationFactory` when the client is the MEAI OpenAI client (or add `IRealtimeSessionOptionsCustomizer` per provider). (3) Log at Warning when a session runs without any speech-started mapping.
- **Verify:** unit test with a fake raw object type named `InputAudioSpeechStartedUpdate`; manual test against an OpenAI-direct deployment.

#### F12. Transcript ordering: late user transcripts land after the assistant's reply
- **Symptom:** For short replies the user's transcript arrives after `AssistantCompleted`; the live view appends it *below* the reply (`ai-chat.js:1264`, `chat-interaction.js:742`, `Chat.cshtml:2066`) and history reloads in the same wrong order because `CreatedUtc` is stamped at transcript arrival (`RealtimeChatSessionRunner.PersistUserTurnAsync`).
- **Fix:** create the user turn at `speech_started` / `input_audio_buffer.committed` (F9's `ItemId`): server emits `ReceiveRealtimeEvent("user_turn_pending", itemId)` so the client inserts a placeholder bubble ("…") in the correct position; persist the user prompt at commit time with an empty text and update it when the transcription completes (`IAIChatSessionPromptStore.UpdateAsync` / interaction equivalent), keeping the earlier `CreatedUtc`. Also stamp assistant turns with the `response.created` time.
- **Verify:** runner test: `UserTurnCommitted → AssistantDone → UserTranscript` must persist the user prompt with an earlier timestamp than the assistant prompt.

#### F13. Barge-in does not truncate the assistant item (model "remembers" unheard text)
- **Symptom:** After an interruption the model believes it delivered the whole answer (its conversation item holds the full generated audio/text), so follow-ups like "wait, repeat that" go wrong. The persisted "partial" turn is also the text generated so far, which is more than the user heard.
- **Root cause:** No `conversation.item.truncate` is ever sent; the runner flushes whatever transcript deltas were received.
- **Fix:** track heard audio: WebRTC — the peer knows `framesSent × 20 ms` minus the browser jitter buffer (~60–100 ms); WS — the client reports `playedMs` in the barge-in hub call (or derive from bytes sent minus the client's buffered tail reported via a small `ReportPlaybackPosition` call every 500 ms). On `speech_started` (barge-in on) send `conversation.item.truncate { item_id, content_index: 0, audio_end_ms }` (add a `TruncateConversationItemRealtimeClientMessage` via `RawRepresentation` JSON for the Azure protocol; MEAI OpenAI supports it via raw). Persist the transcript proportionally (words × heard/total ratio) or wait for `conversation.item.truncated`. The audio delta events already carry `ItemId`; propagate it on `RealtimeConversationEvent`.
- **Verify:** provider fake asserts a truncate message is sent with `audio_end_ms` ≈ frames sent; manual: interrupt mid-answer, ask "what did you just say?" — the model should only reference what was heard.

#### F14. WebRTC pacing drifts and RTP timestamps ignore idle gaps
- **Symptom:** Long replies fall increasingly behind (added latency, reply "ends" later than the transcript), and after an idle gap or a barge-in flush the first packets of the next reply can be time-stretched/warbled by the browser's jitter buffer (NetEQ treats contiguous timestamps after a gap as late packets).
- **Root cause:** `PaceOutgoingAsync` sends exactly one frame per `PeriodicTimer(20 ms)` tick; missed/late ticks are not caught up (Windows timer granularity ~15.6 ms), and `_pc.SendAudio(960, frame)` always advances the RTP timestamp by 960 regardless of wall-clock gaps (`SipSorceryWebRtcRealtimePeer.cs:178–209`).
- **Fix:** keep a media clock: each tick compute `due = floor((now − start) / 20 ms)` and send `due − sent` frames (cap bursts at 3). When the queue was empty for ≥ 1 frame period, advance the RTP timestamp by the elapsed gap (SIPSorcery: use the overload/`SendRtpRaw` with an explicit timestamp, or send Opus DTX/CNG silence frames while idle) and set the RTP marker bit on the first frame of a talk-spurt. Log the queue depth (ms) periodically; expose `QueuedPlaybackMs` for F35.
- **Verify:** unit test the clock math with a fake time provider; manual: 60 s reply → end-of-audio vs transcript-done delta stays < 300 ms; `chrome://webrtc-internals` `jitterBufferDelay` stable.

#### F15. Inbound audio is unbounded and bursts into the provider; 50 tiny messages/s
- **Symptom:** Mic audio accumulates from ICE-connected until the provider session opens (1–3 s) and is then sent in a burst; if the provider send stalls, memory grows without bound. Each 20 ms packet becomes its own `input_audio_buffer.append` JSON+base64 message.
- **Root cause:** `Channel.CreateUnbounded` in the peer; `PumpInputAsync` sends per chunk.
- **Fix:** bounded channel (capacity ≈ 2 s, `BoundedChannelFullMode.DropOldest`); discard audio until `session_ready`; coalesce to ~100 ms (5 packets) before `SendAudioAsync`. Same batching on the WS path is already 170 ms frames — fine.
- **Verify:** unit test on the peer with a fake decoder; provider message rate ≤ 10/s in logs.

#### F16. Output routing on Firefox (and other non-Chromium browsers) can pick the wrong device
- **Symptom:** In Firefox there are no `default`/`communications` output aliases, so `routeRealtimeOutputToDefaultDevice` falls to "first concrete output" — often an HDMI/monitor device the user can't hear. The assistant seems silent.
- **Root cause:** `realtime-audio.js:929–937`, `ai-chat.js:2629–2632`.
- **Fix:** only call `setSinkId` when a `communications` or `default` alias is present (Chromium); otherwise leave the browser default. Add an **Output device** picker to the settings panel (Chromium: `enumerateDevices` outputs; Firefox 116+: `navigator.mediaDevices.selectAudioOutput()`), persist it, and show the active device label next to the *Start speaking* button so surprises are visible. Apply live via `setSinkId`.
- **Verify:** Firefox with two outputs; Chrome on Windows with a headset set as *Default Communications Device* and speakers as *Default Device* (see F28).

#### F17. Fallback and connect UX: 8 s of silence, button lies about the state, repeated on every session
- **Symptom:** After clicking *Start speaking* the button immediately shows *End Conversation* while nothing is connected. On networks where WebRTC can't connect (most PaaS hosts have no UDP ingress, e.g. Azure App Service), every session waits 8 s, then re-prompts `getUserMedia` and restarts on WebSocket with no explanation. Post-connect drops end the session silently.
- **Fix:**
  - Introduce client states (F22): `requesting-mic → connecting → ready/listening`. Button label *Connecting…* with a spinner until `session_ready`.
  - Remember a failed WebRTC attempt in `sessionStorage` (per origin) and go straight to WebSocket for the rest of the browser session; reduce the timeout to 5 s when ICE gathering completed without a relay candidate.
  - Show a small non-blocking notice: "Using compatibility audio mode (WebSocket). Echo cancellation may be weaker; use headphones." Also on the WS path auto-suggest *Allow interruptions: off* when no headset is detected (see §4).
  - Server: add `RealtimeTransportOptions.EnableWebRtc` (default true) so hosts without UDP ingress don't advertise WebRTC; document the hosting requirements (public UDP port range or server-side TURN; SIPSorcery supports `iceServers` for its own srflx/relay candidates).
- **Verify:** Playwright with WebRTC blocked (`--force-webrtc-ip-handling-policy=disable_non_proxied_udp` + no TURN) → WS session ready < 6 s with the notice shown.

#### F18. Two copies of the realtime client (ai-chat.js vs realtime-audio.js) have already drifted
- **Symptom/risk:** ~900 duplicated lines. Drift examples: `ai-chat.js` flips `isConversationMode` before mic permission is granted; help texts differ; `REALTIME_ECHO_HANGOVER_SEC` is dead in the shared module; every fix in this document must be applied twice.
- **Fix:** make `ai-chat.js` consume `window.CoreAIRealtime.attach` (selectors: conversation button = realtime button; `sendStart`/`sendStartWebRtc` call `StartRealtimeConversation(profileId, sessionId, …)`); load `realtime-audio.js` in AI Chat MVC `Chat.cshtml`, `_ChatWidget.cshtml`, and the Blazor `App.razor` (already there). Delete the duplicate methods. Gate with a feature check so the widget degrades gracefully if the script is missing.
- **Verify:** both hosts pass the same Playwright suite.

#### F19. "Auto" language pins the browser locale
- **Symptom:** A bilingual user with an English browser speaking Spanish gets English-forced transcription (garbage) and an English-locked reply directive.
- **Root cause:** client sends `navigator.language` when the setting is *Auto* (`realtime-audio.js:546/680`); `DefaultRealtimeSessionConfigurator.BuildInstructions` then prepends "Always speak and respond in English…" and the transcription `language` is fixed.
- **Fix:** *Auto* → send `null` (Whisper auto-detects; no directive). Only pin when the user explicitly picks a language, or when the profile defines one (add `ChatModeProfileSettings.RealtimeLanguage`). Soften the directive to "respond in the language the user speaks" for Auto.

#### F20. Settings changed mid-session diverge from the server
- **Symptom:** Toggling barge-in during a session changes the client gate but the provider still has `interrupt_response` fixed at start and the server pump keeps its own `AllowInterruption` — the three layers disagree. Turn-detection sliders say "applies to your next session"; the *Echo guard delay* slider is shown on WebRTC although the docs say it is hidden.
- **Fix:** add `UpdateRealtimeSettings(allowInterruption, silenceMs, vadThreshold)` → runner sends `session.update` (turn_detection) and updates `context.AllowInterruption`; or disable those controls while a session is active with a tooltip. Hide the echo-guard slider when the active transport is WebRTC (the module knows `realtimeIsWebRtc`).

#### F21. Push-to-talk Space capture swallows typing
- **Root cause:** `attachRealtimePushToTalk` registers capture-phase handlers with `preventDefault` for Space while a session is active, regardless of the focused element.
- **Fix:** ignore when `e.target` is editable (`input`, `textarea`, `select`, `[contenteditable]`).

#### F22. No conversation state feedback (ChatGPT parity gap)
- **Symptom:** The only UI is a button that toggles *Start speaking / End Conversation*. Users cannot tell whether the mic is live, whether the assistant is thinking, or why speaking during a reply did nothing (barge-in off).
- **Fix:** the shared module exposes `onStateChange({ state, transport, muted, level })` with states `idle | requesting-mic | connecting | listening | user-speaking | thinking | assistant-speaking | muted-while-speaking | reconnecting | ended`. Hosts render a status pill next to the button, a mic level meter (from the worklet), and a one-line hint for `muted-while-speaking` ("Assistant is speaking — wait, or enable interruptions in ⚙"). Add `aria-live="polite"` for the status text and `aria-pressed` on the toggle.

### P2 — robustness and quality

#### F23. Hub upload-stream buffer stalls the connection (WS path)
- SignalR `StreamBufferCapacity` defaults to 10 items; the client sends ~6 × 170 ms frames/s; while the runner's `StartAsync` runs (1–3 s) the buffer fills and the connection's dispatch loop blocks. Raise `StreamBufferCapacity` to ~64 for the chat hubs, or start pumping (and discarding) audio before the provider session is ready.

#### F24. Documentation mismatches
- `realtime-voice.md`: says barge-in on does not pull the remote stream into Web Audio (it does, for the level analyser); says the echo-guard slider is hidden on WebRTC (it is not); overstates that AEC only works on WebRTC. Also `WebRtcRealtimeConversationSink` and ICE docs should describe the client-side ICE delivery once F1 lands, and the hosting requirement (UDP/TURN) from F17.

#### F25. Test gaps
- No tests for: WS sink flush events (F3), ICE delivery (F1), parallel invocations (F2), the gate decision logic (F6/F7/F8 — extract a pure `decideGate(frame, state)` function so it can be unit-tested in Node), pacing clock (F14), transcription-failed / late-commit ordering (F9/F12), non-cancelled response statuses (F10). Add a Playwright cross-browser smoke suite (Chromium + Firefox) using fake media (`--use-fake-device-for-media-stream --use-fake-ui-for-media-stream`; Firefox `media.navigator.streams.fake=true`, `media.navigator.permission.disabled=true`) that: starts a session, asserts `session_ready`, plays a WAV into the fake mic, asserts a user transcript bubble and an assistant bubble, interrupts, asserts flush.

#### F26. Provider session limits and idle sessions
- OpenAI/Azure realtime sessions are capped (60 min) and the WebSocket can be closed server-side; nothing reconnects or informs the user (F4 covers the notification). Add an idle timeout (no user speech for N minutes → `session_ended: idle`) for cost control and a *Resume* affordance that starts a new provider session on the same chat session id.

#### F27. Provider abnormal closes are swallowed
- `AzureRealtimeClientSession.GetStreamingResponseAsync` ends the stream on any `WebSocketException` and `Close` frame without logging the close status/description. Log at Warning with `CloseStatus`/`CloseStatusDescription` and emit a non-benign `Error` event when the status is not `NormalClosure`, so auth expiry / rate-limit closes are diagnosable and visible (via F4).

#### F28. Chrome/Windows "communications" sink may not be the device the user hears
- Windows keeps separate *Default Device* and *Default Communications Device*. Routing to `communications` (which is what fixed AEC coupling) can send the assistant to a headset while the user listens on speakers. Mitigation: F16's output picker + visible device label; document it in the settings help text.

#### F29. Autoplay policy
- The remote `<audio autoplay>` is never `play()`ed explicitly and the gate `AudioContext` is never `resume()`d. Under stricter autoplay settings (Firefox "Block audio and video", kiosk profiles) the assistant is silent with no error. Call `audioEl.play().catch(...)` and `ctx.resume()`; on rejection show a "Click to enable audio" button.

#### F35. Server considers a response "over" before the user has heard it
- `ResponseActivity.Active` clears on `response.done`, which precedes the end of playback by the buffered amount (seconds on WebRTC because of pacing). With barge-in off the server-side half-duplex gate reopens early and only the client gate (weakened by F8) protects the tail. Fix: the peer exposes `QueuedPlaybackMs`; `WebRtcRealtimeConversationSink` reports `playback_drained` back to the runner (callback on the sink interface) and the runner clears `Active` on the later of `response.done` and drained. On WS use `bytesSent / (24000 × 2)` minus a client-reported played position (F13's `ReportPlaybackPosition`).

### P3 — polish

- Hard-coded English strings in the shared module's settings panel; hosts are localizable elsewhere — accept a `strings` map in `attach(opts)`.
- `updateRealtimeButton` assumes `window.DOMPurify` exists; guard it.
- `WebRtcRealtimePeerRegistry.Add` overwrites silently if a second session starts on the same connection; reject or stop the previous one and log.
- `SipSorceryWebRtcRealtimePeer`: `_encodePending` `List<short>.RemoveRange(0, n)` is O(n) per frame — use a ring buffer; `AudioFormat(OPUS, 111, 48000, 2)` advertises stereo while decoding mono — fine for Chrome/Firefox but declare `channels=1` in the SDP `fmtp` for clarity; add Opus PLC (`decode(null)` on sequence gaps) for lossy links.
- iOS Safari: no `setSinkId`, `ScriptProcessorNode` deprecated, audio session routing (earpiece vs speaker) — out of scope for Chrome/Firefox but the worklet from F6 and the state machine from F22 are prerequisites.

---

## 3. Target behavior (ChatGPT parity specification)

### 3.1 Client state machine (shared module, both hosts)

```
idle ──click──▶ requesting-mic ──granted──▶ connecting(webrtc) ──connected + session_ready──▶ listening
   ▲                 │denied                    │timeout/failed
   │                 ▼                          ▼
   └──────────── ended(reason) ◀──── connecting(ws) ──session_ready──▶ listening
listening ──user level──▶ user-speaking ──speech_stopped/commit──▶ thinking ──response_started──▶ assistant-speaking
assistant-speaking ──playback_drained──▶ listening
assistant-speaking + barge-in on + user speech ──▶ user-speaking (flush playback, truncate item)
assistant-speaking + barge-in off + user speech ──▶ muted-while-speaking (hint shown; audio not sent)
any ──ReceiveRealtimeEvent(session_ended) | onclose | ReceiveError──▶ ended(reason) → idle
```

Server events (one client method `ReceiveRealtimeEvent(identifier, type, payload)`): `session_ready`, `user_turn_pending{itemId}`, `speech_started`, `response_started{responseId}`, `response_completed{status}`, `playback_flush`, `session_ended{reason}`.

### 3.2 Barge-in ON — expected behavior per environment

| Environment | Transport | What must happen |
|---|---|---|
| Headset (Chrome/Firefox) | WebRTC | Full duplex. Gate `Auto` (or `Off`). Interruption flushes playback within ~150 ms and truncates the item. No self-answering (no acoustic path). |
| Open office: laptop mic + speakers | WebRTC | Browser AEC + communications-mode routing. Gate `Auto` with adaptive floor. The assistant's own voice must not open the gate (echo residual is typically ≥ 25 dB below near-end speech after AEC3). If the echo test (§4) shows residual above threshold, prompt to turn barge-in off. |
| Open office: standalone USB mic + loud speakers | WebRTC | Same as above but expect more residual; AGC should be off by default in this preset (AGC amplifies echo tails). Recommend barge-in off unless the echo test passes. |
| Any | WebSocket fallback | Flush on `speech_started` (F3). AEC still applies to Web Audio playback in both browsers; gate should also run on the WS path (today it does not) so the WS transport is not the "self-talk" transport. |

### 3.3 Barge-in OFF — expected behavior

- Provider `interrupt_response=false`; server drops mic audio until `response.done` **and** playback drained (F35); client gate closed while the assistant is audible (with hangover, F8) and for the drain tail; listen-grace opens the mic immediately after.
- The user sees *Assistant is speaking* + a muted mic icon; speaking during it shows the hint once (F22). No phantom user bubbles (F9), no split replies.

### 3.4 Device/permission behavior

- Mic and output pickers in the ⚙ panel with live apply; active devices labeled next to the button.
- Chromium: default to the `communications` sink on Windows; elsewhere the default device. Firefox: never force a sink unless the user picked one.
- On `devicechange` (headset unplugged) re-evaluate: if the active mic/output disappeared, end the session with `ended(device_lost)` and offer restart.

---

## 4. Environment presets and an echo self-test (recommended UX addition)

Add a one-click **Audio setup** chooser in the ⚙ panel (persisted per device):

| Preset | Barge-in | Gate | AGC | Noise suppression | Notes |
|---|---|---|---|---|---|
| Headset | On | Off/Auto | On | On | Default when a headset-like device label is detected (`headset`, `earphone`, `AirPods`, `Jabra`…). |
| Laptop speakers | On | Auto | On | On | Default otherwise on Chromium. |
| Room speakers + external mic | Off | Strict | Off | On | Suggested when the echo test fails or the mic label contains `USB`, `Yeti`, `Array`, `Conference`. |

**Echo self-test (≈2 s):** play a known 1 kHz/2 kHz chirp through the routed `<audio>` element (WebRTC path: through a local `MediaStreamDestination` fed to the same element; simpler: an `AudioBufferSource` in the gate context routed to the same sink id), measure the AEC'd mic RMS during the chirp vs. the floor before it. Residual > floor + 6 dB ⇒ recommend *Room speakers* preset (barge-in off). This mirrors how meeting apps decide half-duplex and removes guesswork for open-office users.

---

## 5. Cross-browser support matrix (features the client relies on)

| Feature | Chrome/Edge | Firefox | Notes |
|---|---|---|---|
| `RTCPeerConnection` + Opus | ✅ | ✅ | SIPSorcery interop with Firefox needs testing (stricter SDP: `a=mid`, bundle, `rtcp-mux`); include in the Playwright matrix. |
| `HTMLMediaElement.setSinkId` | ✅ | ✅ 116+ | Firefox exposes outputs only after mic permission; no `default`/`communications` aliases (F16). |
| `selectAudioOutput()` | ❌ | ✅ 116+ | Use for the output picker on Firefox. |
| `AudioContext({sampleRate: 24000})` | ✅ | ✅ | Used by the WS path. |
| `ScriptProcessorNode` | deprecated | deprecated | Replace with AudioWorklet (F6) on both paths. |
| `AudioWorklet` | ✅ | ✅ 76+ | Target for the gate and for WS mic capture. |
| `echoCancellationType`, `voiceIsolation` constraints | Chromium only | ignored | Harmless; keep as `ideal`. |
| AEC reference includes non-WebRTC playback | ✅ (audio service) | ✅ (cubeb duplex) | The WS path is cancellable too; communications-mode coupling is Chrome/Windows specific. |
| `requestAnimationFrame` in hidden tabs | paused | paused | Root cause of F6. |
| Autoplay of remote `<audio>` after click | ✅ (sticky activation) | ✅ unless blocked by policy | F29. |

---

## 6. Prioritized roadmap

| Phase | Items | Outcome |
|---|---|---|
| **1. Correctness of the transport plumbing** | F1, F2, F3, F4, F5 | TURN actually works; ICE trickle and other hub calls work during a session; WS barge-in flushes; the client always knows when a session ended; no first-turn stutter. |
| **2. Audio gate rewrite** | F6, F7, F8, F35, F29 | AudioWorklet gate with adaptive floor, look-ahead, hysteresis, assistant hangover, server-driven drain; works in background tabs; optional. |
| **3. Turn integrity** | F9, F10, F11, F12, F13 | Item-id keyed transcript bookkeeping, failed transcription handling, truncate on barge-in, correct ordering and timestamps, OpenAI-direct parity. |
| **4. UX** | F17, F22, F16, F28, F19, F20, F21, §4 presets + echo test | Connecting/listening/thinking/speaking states, device pickers, sensible fallback, environment presets. |
| **5. WebRTC media quality** | F14, F15 | Media-clock pacing, RTP gap handling, bounded inbound buffer, batched provider appends. |
| **6. Consolidation and verification** | F18, F23, F24, F25, F26, F27 | Single client implementation, docs aligned, unit + Playwright cross-browser suite, idle timeout, provider close diagnostics. |

Each phase is independently shippable; Phase 1 alone removes the failures that make the feature unreliable outside a LAN.

---

## 7. Manual test matrix (run after each phase)

Browsers: Chrome (Windows), Chrome (macOS), Firefox (Windows), Firefox (macOS).
Setups: (a) headset, (b) laptop mic + laptop speakers, (c) USB mic + external speakers at conversational volume.
Transports: WebRTC (LAN), WebRTC via TURN (UDP blocked on the client), WebSocket forced (`EnableWebRtc=false`).

For each cell, with barge-in **on** and **off**:
1. Start → *Connecting…* → *Listening* within 3 s (WebRTC) / 2 s (WS). No premature *End Conversation*.
2. Ask a 30-second question; reply audio is continuous and ends within 300 ms of the transcript.
3. Interrupt at 3 s (barge-in on): old audio stops < 200 ms; new reply starts < 1 s; history shows the partial turn; "what did you just say?" reflects only what was heard.
4. Speak during the reply (barge-in off): nothing is sent; hint appears; after the reply ends the next utterance is captured without clipping; no phantom user bubbles.
5. Stay silent for 60 s during and after a reply in setup (c): no self-answering, no "Thank you" phantom turns.
6. Switch tabs for 20 s mid-conversation and talk: you are still heard (F6).
7. Unplug the headset / kill the network: session ends with a reason within 2 s; *Resume* works.
8. Firefox: assistant audible on the expected device without touching settings (F16).
