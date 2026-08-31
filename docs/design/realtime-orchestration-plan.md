# Realtime (Speech‑to‑Speech) Orchestration — Implementation Plan

**Branch:** `ma/realtime-orcestration`
**Status:** Design only — no feature code in this pass.
**Goal:** Make a `RealtimeChat` AI Profile honor *everything* an AI Profile gives the text chat
client today — system message, tools, knowledge base / data sources, and (where it makes sense)
agent behavior — over an audio↔audio realtime session instead of the text↔text `IChatClient`.
Then integrate that realtime path into the existing AI Chat Session and Chat Interaction UI so a
realtime‑capable profile can transparently replace the STT→ChatClient→TTS "conversation mode"
while keeping history persistence, concurrency protection, and a both‑ends transcript.

---

## 1. Deep dive — how orchestration actually works today

The orchestrator does **two separable jobs**. The seam between them is the crux of this plan.

### 1A. PREPARE — resource‑agnostic, already reusable

`IOrchestrationContextBuilder.BuildAsync(object resource, configure, ct)` runs a reverse‑ordered
`IOrchestrationContextBuilderHandler` pipeline and produces an `OrchestrationContext`. `resource`
is an `AIProfile` **or** a `ChatInteraction` — nothing here is chat‑vs‑realtime specific. Key
handlers, in effect:

| Handler | Stage | What it contributes |
|---|---|---|
| `CompletionContextOrchestrationHandler` | Building | Builds `AICompletionContext` from the resource via `IAICompletionContextBuilder` (base system message, deployment names, `DataSourceId`, temperature, `DisableTools`, …). Seeds `SystemMessageBuilder`. |
| `DataSourceOrchestrationHandler` | Built | If `DataSourceId` set: adds `SearchDataSources` to `MustIncludeTools` and appends data‑source **availability** instructions. |
| `PreemptiveRagOrchestrationHandler` | Built | Extracts queries from the **user message**, runs each `IPreemptiveRagHandler` (data source / document / memory) → vector search → appends retrieved chunks + `[doc:n]` citations into `SystemMessageBuilder`; injects RAG scoping guidance. Has a branch that, when preemptive RAG is *disabled* but tools are on, injects "call the search tool" guidance instead. |
| `AIToolExecutionContextOrchestrationHandler` | Built | Sets `AIInvocationScope.Current.ToolExecutionContext = new AIToolExecutionContext(resource)` and its `ClientName`. |
| Document / Memory / Agent / Security / ExtractedData | both | Additional system‑message and tool contributions. |

After all handlers run, `SystemMessageBuilder` is flushed into
`CompletionContext.SystemMessage`. **The prepared `CompletionContext.SystemMessage` already
contains base prompt + preemptive knowledge + RAG guidance.** This whole stage is independent of
how the completion is executed.

### 1B. EXECUTE — chat‑specific tail

Inside `DefaultOrchestrator.ExecuteStreamingAsync`:

1. `IToolRegistry.GetAllAsync(completionContext)` → `IReadOnlyList<ToolRegistryEntry>`; each entry
   carries `CreateAsync: Func<IServiceProvider, ValueTask<AITool>>`.
2. **Scoping/planning** (per turn): below `ScopingThreshold` inject all; else lightweight relevance
   scoping or a full LLM planning call (when MCP tools present or over `PlanningThreshold`). Chosen
   entries are stashed in `CompletionContext.AdditionalProperties[ScopedEntriesKey]`.
3. `IAICompletionService.CompleteStreamingAsync` → `NamedAICompletionClient`:
   - `GetChatOptionsAsync` runs `IAICompletionServiceHandler`s, notably
     `FunctionInvocationAICompletionServiceHandler.ConfigureAsync`, which **materializes** scoped
     entries → `AIFunction`s into `ChatOptions.Tools` (dedupe by name; per‑user access checks for
     Chat Interactions; system/hidden tools bypass the check).
   - `BuildClientAsync` wraps `IChatClient` with `UseFunctionInvocation` (the tool loop),
     resilience, logging, distributed cache, telemetry.
   - Streams `ChatResponseUpdate`. `FunctionInvokingChatClient` runs the tool loop.

### 1C. The ambient contract tools depend on (`AIInvocationScope`)

Tools are singletons; they receive **no** invocation identity from the model. They read
`AIInvocationScope.Current` (an `AsyncLocal<AIInvocationContext>`), which carries:
`ToolExecutionContext` (with `Resource` = the `AIProfile`), `DataSourceId`, `CompletionContext`,
`ChatSession`/`ChatInteraction`, `NextReferenceIndex()`, and `ToolReferences`. E.g.
`DataSourceSearchTool.InvokeCoreAsync` reads `AIInvocationScope.Current.DataSourceId`, uses
`arguments.Services` for DI, vector‑searches, and populates `ToolReferences` for citations.

The scope is opened **once per hub invocation**: `HandleSendMessageAsync` calls
`using var invocationScope = AIInvocationScope.Begin();` and `AIChatResponseHandler` then populates
its fields. Because `AsyncLocal` flows into tasks started *within* the scope, tool calls made deep
inside the completion stream see the correct context.

**Implication for realtime:** any realtime tool loop must run inside an equally‑established
`AIInvocationScope`, and must pass `AIFunctionArguments.Services` = a request DI scope. Then every
existing tool works unchanged.

### 1D. Chat session / hub path (text + current voice)

`AIChatHubCore.SendMessage` → `HandleSendMessageAsync` (opens the scope) → `ProcessChatPromptAsync`:
resolve/create session, persist user prompt, build history, resolve `IChatResponseHandler` via
`IChatResponseHandlerResolver.Resolve(responseHandlerName, chatMode)`; the default
`AIChatResponseHandler` builds the orchestration context, resolves the orchestrator by
`profile.OrchestratorName`, and returns `ChatResponseHandlerResult.Streaming(...)`. The hub then
consumes the stream: appends text, collects references, runs output security on the full text,
persists the assistant `AIChatSessionPrompt`, runs `IAIChatSessionHandler.MessageCompletedAsync`,
saves the session. Concurrency is enforced by `StoreCommitterHubFilter`; rate limits and title
generation happen here too.

**Current voice "conversation mode"** (`StartConversation` → `RunConversationLoopAsync` →
`TranscribeConversationAsync` → `ProcessConversationPromptAsync`) is *not* a separate brain: it
runs STT, then feeds each final utterance's **text** into `HandleSendMessageAsync` (full persist +
orchestration pipeline), and pipes assistant tokens through a sentence buffer into TTS. This is the
laggy STT→LLM→TTS path we want to replace with a realtime session when the model supports it.

### 1E. Realtime infrastructure already merged (PR #158)

- `IAIClientFactory.CreateRealtimeClientAsync(deployment)` / `IAIClientProvider.GetRealtimeClientAsync`
  → `IRealtimeClient`. OpenAI via MEAI's `OpenAIRealtimeClient`; Azure GA via a custom WebSocket
  transport in `…/OpenAI.Azure/Realtime/` (`AzureRealtimeClient/Session/Protocol`).
- `IRealtimeClientSession`: `SendAsync(RealtimeClientMessage)`,
  `GetStreamingResponseAsync() → IAsyncEnumerable<RealtimeServerMessage>`, and `Options`.
- The Azure protocol **already** serializes `RealtimeSessionOptions.Tools` (AIFunctions →
  `{type:function,name,description,parameters}`), `ToolMode`, parses `function_call` output items
  into `FunctionCallContent`, and writes `function_call_output` from a `FunctionResultContent`
  conversation item. `session.update` re‑config is supported (`SessionUpdateRealtimeClientMessage`).
- `RealtimeVoiceBridge` (playground) only sets `Instructions`/`Voice`/formats/VAD/transcription —
  **no tools, no RAG, no profile, no persistence.**
- Model bits: `AIProfileType.RealtimeChat`, `AIProfile.RealtimeDeploymentName`,
  `AIDeploymentPurpose.Realtime`; voice resolution via `IRealtimeVoiceResolver`.
- MEAI 10.9.0 ships an experimental `FunctionInvokingRealtimeClientSession` (the streaming analog of
  `FunctionInvokingChatClient`).

### 1F. The core tension

`IChatClient` is turn‑based request/response: the orchestrator can re‑scope/plan **per turn**, and
the tool loop lives inside the completion service, returning one `IAsyncEnumerable<ChatResponseUpdate>`.
`IRealtimeClient` is a **persistent duplex audio session configured once**: tools, turn‑taking, and
interruptions arrive as async events; there is no up‑front user query, and no single return stream
(we need an audio *sink* in and audio + transcript + events out). Matching chat capabilities
therefore needs: (a) a **realtime tool‑invocation loop**, and (b) RAG surfaced as a **callable
search tool** rather than an up‑front injection.

---

## 2. Decision — recommended approach

**Reuse PREPARE verbatim; add a sibling realtime executor; extract the two shared seams.** This is a
blend of the three options, chosen because it maximizes reuse without distorting either contract.

### 2.1 Why not each pure option

- **Option 1 (extract PREPARE into a shared abstraction):** Mostly *already done*. PREPARE is the
  resource‑agnostic `IOrchestrationContextBuilder` pipeline; it produces an `OrchestrationContext`
  with no dependency on chat execution. We should *recognize and reuse* this seam, not rebuild it.
- **Option 2 (parallel `IRealtimeOrchestrator` / strategy behind a common front‑end):** Right
  instinct on the executor, but the front‑end cannot be *the same* `IOrchestrator`.
  `IOrchestrator.ExecuteStreamingAsync` returns a single `IAsyncEnumerable<ChatResponseUpdate>` —
  a request/response streaming shape. Realtime is duplex with lifecycle events and audio; forcing
  it behind that signature is a leaky abstraction. A **sibling interface that shares PREPARE** is the
  clean SOLID answer (segregated interfaces, one per interaction shape).
- **Option 3 (FunctionInvokingRealtimeClientSession + profile→options configurator composed in the
  host, orchestrator stays chat‑only):** This is the *execution half* of the right answer, but doing
  the composition "in the host" would duplicate/scatter PREPARE and scope setup. Keep PREPARE in the
  shared builder and wrap execution in a small realtime orchestrator that internally uses the
  session decorator + configurator.

### 2.2 The recommended shape

```
                 ┌─────────────────────────────────────────────┐
                 │  IOrchestrationContextBuilder  (UNCHANGED)   │   ← PREPARE, resource-agnostic
                 │  handler pipeline → OrchestrationContext     │      (profile OR interaction)
                 └───────────────┬───────────────┬─────────────┘
                                 │               │
        ┌────────────────────────▼───┐   ┌───────▼──────────────────────────┐
        │ DefaultOrchestrator (chat) │   │ DefaultRealtimeOrchestrator (new) │
        │ scope+plan per turn        │   │ configure-once realtime session   │
        │ → IAICompletionService     │   │ → IRealtimeSessionConfigurator    │
        │   (FunctionInvokingChat…)  │   │ → FunctionInvokingRealtimeSession │
        └───────────┬────────────────┘   └───────────────┬──────────────────┘
                    │  materialize tools                  │  materialize tools
                    └──────────────┬──────────────────────┘
                                   ▼
                     IToolMaterializer (new, extracted)   ← scoped entries → List<AITool>
```

Two seams are **extracted for DRY**, everything else is reused:

1. **`IToolMaterializer`** — pulls the "scoped `ToolRegistryEntry` → `List<AITool>`" logic out of
   `FunctionInvocationAICompletionServiceHandler.ConfigureAsync` (factory invocation, name dedupe,
   Chat‑Interaction per‑user access checks). Both the chat completion handler and the realtime
   configurator call it, so tool selection stays identical across paths.
2. **PREPARE** — reused as‑is through `IOrchestrationContextBuilder`.

New realtime execution lives behind **`IRealtimeOrchestrator`** (sibling of `IOrchestrator`), so the
chat orchestrator and completion service are untouched.

---

## 3. Requirement‑by‑requirement design

### 3.1 System message (incl. preemptive knowledge) → `RealtimeSessionOptions.Instructions`

- PREPARE already composes `CompletionContext.SystemMessage`. `DefaultRealtimeSessionConfigurator`
  sets `RealtimeSessionOptions.Instructions = CompletionContext.SystemMessage`.
- **Preemptive RAG nuance:** in a live audio stream there is no user message at PREPARE time. Build
  the realtime context with an **empty `UserMessage`**. `PreemptiveRagOrchestrationHandler.BuiltAsync`
  already early‑returns when `UserMessage` is empty, so no preemptive vector search runs — correct,
  because RAG must be tool‑driven in realtime.
- **Gap to close:** that same early‑return also means the "use the `SearchDataSources` tool"
  guidance is *not* injected. Fix by signalling realtime mode into PREPARE:
  - Add `OrchestrationContext.ExecutionMode` (`Chat` | `Realtime`), set via the `configure` delegate.
  - In `PreemptiveRagOrchestrationHandler`, when `ExecutionMode == Realtime` and tools are enabled,
    **skip preemptive search but always inject the tool‑search guidance** (reuse the existing
    `RagToolSearch*` templates), regardless of empty user message. `DataSourceOrchestrationHandler`
    already injects availability text + `MustIncludeTools` from `DataSourceId` alone, so that part
    needs no change. (Alternative: a dedicated `RealtimeRagGuidanceHandler`; prefer the small branch
    to avoid a parallel code path.)

### 3.2 Tools → session tools **and** execution (the realtime tool loop) + scope

- **Selection:** resolve `IToolRegistry.GetAllAsync(completionContext)`; realtime configures tools
  **once**, so use the "inject all (+ `MustIncludeTools`), no planner" path — the analog of the
  chat `count ≤ ScopingThreshold` branch. Materialize via `IToolMaterializer` → `List<AITool>` →
  `RealtimeSessionOptions.Tools`; set `ToolMode = Auto`.
- **Execution:** `FunctionInvokingRealtimeClientSession` (our decorator over `IRealtimeClientSession`)
  owns the `AIFunction`s and the loop. On a `FunctionCallContent` server message it:
  1. Finds the matching `AIFunction` by name.
  2. Invokes `await fn.InvokeAsync(new AIFunctionArguments(args){ Services = requestScope }, ct)`.
  3. Sends `CreateConversationItemRealtimeClientMessage(new RealtimeConversationItem([new
     FunctionResultContent(callId, result)]))` then `CreateResponseRealtimeClientMessage()` to make
     the model continue speaking. (The Azure protocol already serializes both.)
  4. Surfaces a neutral `tool-invoked` event to the orchestrator; audio/transcript pass through.
  - Build our own decorator rather than depending on MEAI's experimental one, so we control (a)
    `AIFunctionArguments.Services`, (b) `AsyncLocal` flow, and (c) error mapping. Keep the seam so
    MEAI's can be swapped in later.
- **`AIInvocationScope` establishment (critical):** `DefaultRealtimeOrchestrator` opens/uses an
  `AIInvocationScope` and **starts the session pump inside it**, populating `ToolExecutionContext`
  (`Resource` = profile), `DataSourceId`, `CompletionContext`, and `ChatSession`/`ChatInteraction`
  exactly like `AIChatResponseHandler` does. Because the decorator invokes tools inline while the
  orchestrator enumerates `GetStreamingResponseAsync()` *within* that scope, `AsyncLocal` flows and
  every existing tool (e.g. `DataSourceSearchTool`, `SearchDocumentsTool`) works unchanged. One
  ambient context per session is fine (audio is turn‑based); `NextReferenceIndex()`/`ToolReferences`
  accumulate across the session for citations.

### 3.3 Knowledge base / data sources in realtime

- **Preemptive:** skipped by design (no up‑front query) — falls out of §3.1 automatically.
- **Search‑tool‑driven:** `SearchDataSources` (`DataSourceSearchTool`) and `SearchDocuments`
  (`SearchDocumentsTool`) become session tools. They already read `DataSourceId` from
  `AIInvocationScope.Current`, embed the query, vector‑search, return chunks + `[doc:n]` citations,
  and fill `ToolReferences`. They work with **zero changes** once §3.2's scope + `arguments.Services`
  are in place. §3.1's guidance tells the model to call them.

### 3.4 What genuinely does **not** port (and why that's acceptable)

- **Per‑turn tool scoping / LLM planning.** Realtime configures tools once; there is no
  server‑controlled per‑turn boundary at which to re‑plan, and a planning round‑trip per turn would
  re‑introduce exactly the latency we are removing. Inject the full toolset once. *Future option:*
  mid‑session `session.update` (already supported by the transport) to swap a tool subset when
  intent shifts — noted, not built.
- **Multi‑step agent orchestration loop.** The chat orchestrator's planning/expansion loop and
  sub‑agent recursion assume turn‑based completions. Agent‑as‑a‑tool (`AgentProxyTool`) can still be
  a session tool because it is just an `AIFunction` that internally runs its own completion (and
  `AIInvocationContext.AgentInvocationDepth` still guards recursion). What does not port is the
  realtime orchestrator itself doing planning/expansion — acceptable for a latency‑sensitive voice UX.
- **Chat‑tail features:** distributed response caching, whole‑response output security, and per‑turn
  reference‑collection cadence differ. Re‑applied at the transcript level in Part 2 where sensible.

### 3.5 Deployment resolution

Resolve the realtime deployment via
`deploymentManager.ResolveOrDefaultAsync(AIDeploymentPurpose.Realtime, profile.RealtimeDeploymentName)`
(not the chat deployment). `CompletionContext` still supplies system message, `DataSourceId`, tool
config, temperature, etc.

---

## 4. Concrete interfaces / types, and where they live

**`CrestApps.Core.AI.Abstractions`**
- `IRealtimeOrchestrator` — sibling of `IOrchestrator`. **Not** `IAsyncEnumerable<ChatResponseUpdate>`.
  Proposed contract:
  ```csharp
  Task RunAsync(RealtimeOrchestrationRequest request,
                IRealtimeChannel channel,
                CancellationToken ct);
  ```
  where `IRealtimeChannel` abstracts **audio in** (an `IAsyncEnumerable<ReadOnlyMemory<byte>>` or a
  writer) and **out** a stream of provider‑neutral `RealtimeEvent`s.
- `RealtimeEvent` (+ subtypes / discriminated shape): `AudioDelta`, `UserTranscript(partial/final)`,
  `AssistantTranscript(delta/final)`, `ToolInvoked`, `TurnStarted/SpeechStarted`, `Error`,
  `ResponseDone`. Keeps hosts free of MEAI experimental types.
- `IRealtimeSessionConfigurator` — maps prepared `OrchestrationContext` → `RealtimeSessionOptions`.
- `OrchestrationContext.ExecutionMode` (new enum property; default `Chat`).

**`CrestApps.Core.AI`**
- `DefaultRealtimeOrchestrator : IRealtimeOrchestrator` — PREPARE (reuse builder) → configurator →
  open session → wrap in `FunctionInvokingRealtimeClientSession` → pump under `AIInvocationScope` →
  emit `RealtimeEvent`s; owns the request DI scope for tool `Services`.
- `FunctionInvokingRealtimeClientSession : IRealtimeClientSession` — the realtime tool loop.
- `IToolMaterializer` / `DefaultToolMaterializer` — extracted from
  `FunctionInvocationAICompletionServiceHandler` (which is refactored to call it).
- `DefaultRealtimeSessionConfigurator` — Instructions from `CompletionContext.SystemMessage`; Voice
  via `IRealtimeVoiceResolver`; input/output PCM formats; VAD; `Tools` via `IToolMaterializer`;
  `ToolMode.Auto`; `Model`/deployment from `AIDeploymentPurpose.Realtime`.
- Small edit to `PreemptiveRagOrchestrationHandler` for the §3.1 realtime guidance branch.
- DI registration for the above; a `realtime` orchestrator entry if we choose to route via a name.

**Reused unchanged:** `IOrchestrationContextBuilder` + all context/RAG handlers, `IToolRegistry`,
every `AIFunction` tool, `AIInvocationScope`/`AIInvocationContext`, `IRealtimeVoiceResolver`,
`IAIClientFactory.CreateRealtimeClientAsync`, the Azure/OpenAI transports.

---

## 5. Part 2 — integrating realtime into Chat Sessions & Chat Interactions

**Objective:** when the profile/model is realtime‑capable, replace the STT→ChatClient→TTS
"conversation mode" with a realtime session, **without losing** history persistence, concurrent‑
connection protection, rate limiting, or the both‑ends transcript in the existing Chat UI.

### 5.1 Why we cannot just reuse `HandleSendMessageAsync`

Current voice mode reuses the text pipeline by feeding STT **text turns** into
`HandleSendMessageAsync`. Realtime has no discrete text turn to inject — it is a duplex session with
its own VAD/turn‑taking. So realtime needs its own hub entry, but one that **reuses the same session
object and hub scope** so persistence/concurrency still apply.

### 5.2 New hub method (sibling of `StartConversation`)

`StartRealtimeConversation(profileId, sessionId, IAsyncEnumerable<string> audioChunks, audioFormat, language)`:
1. `using var scope = AIInvocationScope.Begin();`
2. Resolve profile; authorize; **capability‑gate** (see §5.4).
3. `GetOrCreateSessionAsync(...)` — reuses session creation, rate limiting, and the
   `StoreCommitterHubFilter` concurrency/commit behavior already wired at hub level.
4. Build the realtime orchestration context from the session's profile (`ExecutionMode = Realtime`),
   populate `AIInvocationScope` fields (session, data source, completion context).
5. Run `DefaultRealtimeOrchestrator.RunAsync`, bridging:
   - browser audio in → session audio sink,
   - `AudioDelta` → `ReceiveConversationAssistantAudio` (new client method; binary),
   - `UserTranscript` / `AssistantTranscript` → **existing** `ReceiveConversationUserMessage` /
     `ReceiveConversationAssistantToken` so the current Chat UI transcript renders unchanged,
   - `Error` → `ReceiveError`.

### 5.3 Transcript persistence (history + both‑ends transcript)

Persist at **turn boundaries**, driven by transcript events (mirrors `ProcessChatPromptAsync`, but
event‑driven instead of channel‑driven):
- On **user final transcript** (`InputAudioTranscriptionCompleted`): create a User
  `AIChatSessionPrompt`; on the first user turn, run `GenerateSessionTitleAsync`.
- On **assistant final transcript** (`OutputAudioTranscriptionDone`): create an Assistant
  `AIChatSessionPrompt` with `References` from `AIInvocationScope.Current.ToolReferences`; run
  `IAIChatSessionHandler.MessageCompletedAsync`; best‑effort `IOutputSecurityFilter` on the
  transcript text; `SaveChatSessionAsync`.
- On **barge‑in / interruption** (VAD `speech_started` cancels the assistant): persist the partial
  assistant transcript actually spoken so history reflects reality.
Persisting per turn (not only at session end) means a dropped connection still leaves correct
history, and the existing concurrency watch/commit filter runs at each hub‑method boundary as today.

### 5.4 Capability detection & the swap

- **Primary signal (explicit):** `profile.Type == RealtimeChat`, or a `ChatModeProfileSettings`
  option (e.g. `PreferRealtime`) on a normal Chat profile.
- **Gate (capability):** a small `IRealtimeCapabilityResolver` that reports whether a
  `AIDeploymentPurpose.Realtime` deployment resolves for the profile (via `RealtimeDeploymentName`
  or default) and the provider supports realtime.
- **UI behavior:** when a profile is realtime‑capable and the user picks voice/conversation, the
  client calls `StartRealtimeConversation` instead of `StartConversation`. Recommend keeping the
  explicit opt‑in primary (predictable cost/behavior) rather than silently switching every
  realtime‑capable model — but the capability resolver makes an "auto" policy a one‑line change if
  desired later.

### 5.5 What differs from text chat (call out for review)

No per‑turn planning/scoping; deferred/webhook response handlers and streaming reference cadence
differ; output security applies to the transcript rather than a buffered full response. All
acceptable for a voice UX and documented.

---

## 6. Phased build order

| Phase | Deliverable | Verification gate |
|---|---|---|
| **0. Seams** | Extract `IToolMaterializer` from `FunctionInvocationAICompletionServiceHandler` (handler refactored to call it). Add `OrchestrationContext.ExecutionMode`. | Existing chat/tool tests stay green (no behavior change). New parity unit test. |
| **1. Configurator** | `IRealtimeSessionConfigurator` + `DefaultRealtimeSessionConfigurator`. §3.1 RAG‑guidance branch in `PreemptiveRagOrchestrationHandler`. | Pure‑mapping unit tests. |
| **2. Tool loop** | `FunctionInvokingRealtimeClientSession` decorator. | Unit tests with a **fake `IRealtimeClientSession`** (below). |
| **3. Orchestrator** | `DefaultRealtimeOrchestrator` + `IRealtimeOrchestrator`/`RealtimeEvent`/`IRealtimeChannel`; DI wiring; scope establishment. | Orchestrator unit test: prepared context → session options correct; tool call round‑trips under scope. |
| **4. Standalone proof** | Route `RealtimeController`/`RealtimeVoiceBridge` for a `RealtimeChat` profile through the orchestrator (tools + RAG, no persistence yet). | Manual playground: spoken tool call + data‑source question with spoken citations. |
| **5. Chat integration** | `StartRealtimeConversation` hub method, capability resolver, transcript persistence, client audio method. | Hub test with fake realtime client: both‑ends transcript persisted, references attached, title generated, per‑turn save. |

---

## 7. Verification strategy

**Unit**
- `DefaultRealtimeSessionConfigurator`: prepared context ⇒ `Instructions == SystemMessage`, tools
  materialized (same set as chat via shared `IToolMaterializer`), voice resolved, VAD/formats set,
  realtime deployment chosen.
- `FunctionInvokingRealtimeClientSession` with a **fake session** that emits a `FunctionCallContent`:
  assert (a) the matching `AIFunction` is invoked with the decoded args, (b) inside the tool
  `AIInvocationScope.Current` is non‑null and `ToolExecutionContext.Resource` is the profile,
  (c) a `function_call_output` conversation item + a `response.create` are sent, (d) result text is
  correct, (e) unknown‑tool and tool‑throws paths surface an error event without tearing down the
  session.
- `IToolMaterializer` parity: identical entries produce identical `AITool` sets for the chat handler
  and the realtime configurator (incl. Chat‑Interaction access‑check behavior).
- RAG guidance: realtime PREPARE injects search‑tool instructions + `SearchDataSources` in tools and
  runs **no** preemptive vector search.

**Integration / E2E**
- Extend existing `AzureRealtimeTests` / `AIClientProviderRealtimeTests`.
- Playground (Phase 4): a `RealtimeChat` profile with (i) a trivial echo/weather tool and (ii) a
  data‑source‑backed profile — verify a spoken tool call and a spoken, cited answer from the KB.
- Hub (Phase 5): drive `StartRealtimeConversation` with a fake realtime client and scripted
  transcript/tool events; assert user+assistant prompts persisted, references attached, title
  generated, session saved at each turn boundary, and barge‑in persists the partial assistant turn.

---

## 7a. As-built notes (implementation delta)

The server-side implementation landed with two deliberate refinements to this plan:

1. **Tool loop uses Microsoft.Extensions.AI's own middleware, not a hand-rolled decorator.**
   MEAI 10.9.0 ships `FunctionInvokingRealtimeClient` / `RealtimeClientBuilder.UseFunctionInvocation`,
   the realtime analog of `FunctionInvokingChatClient`, which accepts an `IServiceProvider`
   (`FunctionInvocationServices`) used as each tool's `AIFunctionArguments.Services` — exactly the text
   path's model. `DefaultRealtimeOrchestrator` wraps the provider client with it and passes the request
   scope, so tools resolve dependencies and observe the ambient `AIInvocationScope` unchanged. The Azure
   transport already emits `ResponseOutputItemDone` with `FunctionCallContent`, which the middleware
   requires. Tools are set on both `RealtimeSessionOptions.Tools` (advertised to the model) and
   `FunctionInvokingRealtimeClient.AdditionalTools` (resolved for invocation). This is more DRY and
   better-maintained than a custom decorator; `IRealtimeConversation` remains the provider-neutral seam.

2. **Zero-edit RAG guidance.** A dedicated `RealtimeRagGuidanceHandler` (gated on
   `OrchestrationExecutionMode.Realtime`) injects search-tool guidance; existing handlers are untouched.

**Types added.** Abstractions: `OrchestrationExecutionMode`, `IToolMaterializer`,
`IRealtimeSessionConfigurator`, `IRealtimeOrchestrator` + `IRealtimeConversation` + `RealtimeConversationEvent`,
`IRealtimeCapabilityResolver`. Primitives (`CrestApps.Core.AI`): `DefaultToolMaterializer` (extracted
from the completion handler), `DefaultRealtimeSessionConfigurator`, `RealtimeRagGuidanceHandler`,
`DefaultRealtimeOrchestrator`, `DefaultRealtimeConversation`, `DefaultRealtimeCapabilityResolver`. Chat
(`CrestApps.Core.AI.Chat`): `IRealtimeConversationSink`, `RealtimeChatRunContext`,
`RealtimeChatSessionRunner`, and the hub `StartRealtimeConversation` method + `SignalRRealtimeConversationSink`.

**Verified.** 15 new unit tests (materializer parity, configurator mapping, RAG-guidance branch, the
realtime tool loop under scope with the real MEAI middleware and a fake session, capability resolver, and
the runner's both-ends transcript persistence + barge-in). Full suite: **2837 passed, 0 failed**. Both
sample hosts build; the MVC host boots and serves requests with the new DI graph.

**Playground (manual test of the orchestrator) — done.** The MVC realtime test page
(`/Realtime`, `RealtimeController`) now has a **RealtimeChat profile** selector. Selecting a profile routes
the session through `RealtimeVoiceBridge.HandleProfileAsync` → `IRealtimeOrchestrator`, so the live
speech-to-speech session honors the profile's system message, tools, and knowledge base (RAG via the
search tool). Leaving it on "Raw deployment" keeps the original bare-instructions path. The existing
`realtime-test.js` mic-capture/playback/transcript client is reused unchanged except for sending
`profileId`. This is the fastest way to manually verify the orchestrator end to end.

**Not yet done (chat-app UI).** Wiring the *product* chat UI's voice affordance to call
`StartRealtimeConversation` (mic capture + assistant-audio playback + transcript inside the chat surface)
for a realtime-capable profile. The hub method, sink, capability gate, and persistence are all in place and
tested; this is the remaining front-end integration in the chat app. The `SpeechStarted` barge-in signal is
a server-side no-op today (the client mutes its own playback); a dedicated client method can be added then.

**Manual test steps (playground).** (1) Create an AI provider connection and a deployment whose purpose
includes **Realtime** (e.g. Azure OpenAI `gpt-realtime`). (2) Create a **RealtimeChat** profile pointing at
it; optionally give it tools and/or a data source. (3) Sign in, open **/Realtime**, pick the profile, choose
a voice, click **Start talking**, and speak. Verify: spoken answers, the both-ends transcript, a tool being
invoked when you ask something that needs it, and a cited answer when you ask about the data source.

## 8. Open questions for review

1. **Auto‑swap policy:** explicit opt‑in only (`RealtimeChat` type / `PreferRealtime`), or auto‑use
   realtime whenever the resolved model supports it? (Recommend explicit‑primary + capability gate.)
2. **`IRealtimeChannel` shape:** audio‑in as `IAsyncEnumerable<ReadOnlyMemory<byte>>` vs a writer/pipe
   — pick to match the existing hub streaming ergonomics.
3. **Output security on audio:** filter the transcript post‑turn (can't unsay audio) — acceptable, or
   do we need pre‑response gating (e.g., `ToolMode`/instruction constraints)?
4. **Citations UX in voice:** `[doc:n]` markers are spoken awkwardly; surface citations only in the
   visible transcript, not the audio instructions? (Likely a template tweak for realtime mode.)
