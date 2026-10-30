# AI Memory — Deep Dive, Findings, and a Tiered Memory Design

**Branch:** `claude/ai-memory-deep-dive-09d7e0`
**Date:** 2026-10-29
**Status:** P0 (M1–M5) implemented 2026-10-29. Session memory and past conversations (the core of P1–P3)
implemented and validated in the MVC sample on 2026-10-30, together with two leaks found on the way. See the two
progress sections below. P4, P5 and M6 remain proposals.
**Goal:** Explain how memory works today across AI profiles, chat sessions, Chat Interactions and realtime
voice. List the bugs and design gaps. Then propose a memory system modeled on current production systems
(ChatGPT, the Claude apps, the Claude API, Microsoft.Extensions.AI, Letta, Mem0 and Zep) that remembers what was
said earlier in a long conversation, what happened in past conversations, and durable facts about the user,
without sending the whole transcript on every turn.

---

## P0 progress (2026-10-29)

| Finding | What shipped |
|---|---|
| **M1** | Indexing and search both resolve the embedding deployment through `GetMemoryEmbeddingDeploymentName()` (metadata first, then the profile property). A regression test covers a metadata-only profile. |
| **M2** | `IPreemptiveRagHandler.RunsWhenPreemptiveRagIsDisabled` (default-interface member, `false`). The memory handler returns `true`, so it runs when site-wide preemptive RAG is off. Data-source search instructions are injected only when a knowledge handler is usable, not for memory alone. |
| **M3** | `AIMemorySearchService` falls back to a term-overlap ranking over `IAIMemoryStore.GetByUserAsync` (up to 200 memories, Lucene tokenizer) when there is no usable index or the index returns nothing for the user. A misconfigured index warns once per profile name. |
| **M4** | All five tail checks (`NamedAICompletionClient`, `ClaudeOrchestrator`, `AzureOpenAICompletionClient`, its Responses path, `AzureOpenAIChatMessageConverter`) use `> 0`. Prompt tests updated: `1` now keeps one message. |
| **M5** | `AIMemoryEntryHandler` moved into `CrestApps.Core.AI` and queues writes to `AIMemoryIndexingQueue`, drained by `AIMemoryIndexingBackgroundService`. It is registered by `AddCoreAIMemoryIndexing()`, which `AddAIMemory()` calls. **Deviation:** it is *not* registered by `AddCoreAIMemory()`, because the Orchard Core module calls that and ships its own deferred indexing handler, which would index every memory twice. |

Verification: full suite 5013/5013 passing; the MVC sample host starts with the new registrations. Not
verified: indexing against a live vector provider, and the Blazor host at run time (it builds).

---

## Session and cross-session memory progress (2026-10-30)

| Item | What shipped | Deviation from §5 |
|---|---|---|
| **T1 session memory** (§5.2a, c) | `ConversationMemoryOrchestrationHandler`: messages outside `PastMessagesCount` are folded into a running summary (`AIConversationMemory` on the session or interaction) every 6 dropped messages; the unsummarized ones are shown verbatim; up to 3 older turns are recalled by shared terms; `search_conversation` searches the rest from the invocation scope. Profile switch `EnableSessionMemory`; on by default for chat interactions. | The summary is updated **before** the turn on the utility model, not in the background after it: about one extra utility call every three turns. Recall is term-overlap over the in-memory history, not a vector index of turns. The §5.1 token-budgeted assembler is **not** done: the window is still `PastMessagesCount`. |
| **T2 past conversations** (§5.3) | `search_past_conversations` and `list_recent_conversations`, scoped to the signed-in user and the profile; ownership checked on every session read; `AIChatSessionQueryContext.UserId`. Profile switch `EnablePastConversations`. | No episode summary is written at session close: the tools read the running summary when there is one and the transcript otherwise. No ambient digest. Chat interactions get T1 only: in the samples they are a shared admin workspace keyed by user name, not user id. |
| **Privacy** (§5.6) | The summary is collected, redacted and stripped with the transcript. | — |
| **Leak: session ids** | `AIChatHubCore.AuthorizeSessionAsync`; `LoadSession`, `RateMessage`, notification actions and `GetOrCreateSessionAsync` call it. Sample hosts allow the `Admin` policy. | Not in the original review: found while checking isolation. |
| **Leak-adjacent bug: blank memories** | `memory-context-header` and the new template are rendered from dictionaries. | Not in the original review: Fluid denies member access to anonymous types, so retrieved user memories had reached the prompt as blank lines. |

**Validated in the MVC sample against Azure OpenAI gpt-4.1-mini**, with two non-admin users (`CrestApps:Users`):

- Long session (window 4): a code name and budget from turn 1 were recalled correctly at turn 7. The summary stored
  on the session kept both verbatim. The same check passed in a Chat Interaction.
- Past conversations: with user memory off, a fact from one of alice's chats was answered in a new chat through
  `search_past_conversations`, with 4 of alice's conversations in scope.
- Isolation: bob's same question searched only bob's 3 conversations and found nothing. Bob did not get alice's
  saved memory. `LoadSession` for alice's sessions returned "Session not found" to bob and to an anonymous visitor.
  A message bob sent to alice's session id went into a new session of bob's, and alice's transcript was unchanged.
  The widget, still holding alice's session id in localStorage after bob signed in, opened empty. An admin could
  still open alice's session.

**Still open:** the §5.1 token-budget assembler, background summarization, a vector index of turns, episode
summaries at close, T4 background extraction and consolidation, scoping user memory per profile, and realtime voice
(§5.7: voice sessions still start without history). Also M6.

**Why summarization is not after the answer yet (tried 2026-10-30).** Running it from
`IAIChatSessionHandler.MessageCompletedAsync`, where data extraction already runs, showed the answer about 1.8 s
sooner on a summary turn, but the stream stays open while it runs and the send button is a Stop button until the
stream closes. In the Core hubs the turn runs in SignalR's invocation scope (`ExecuteInScopeAsync` defaults to the
hub's provider), which is disposed when the stream is cancelled, so one click in that window lost the summary
**and** the whole exchange (`ObjectDisposedException`, nothing committed). Running the summary alongside the answer
instead would write usage records concurrently with the turn's own store session. Both need the hubs to run the
post-answer work in a scope of their own first; until then the summary is made before the answer, about one
utility call every three turns. The same Stop-click loss already applies to data extraction in the Core hubs.
CrestApps.OrchardCore is not affected by the scope part: its hubs open their own `ShellScope`.

**CrestApps.OrchardCore after the next Core preview.** Its `AIChatHub` derives from `AIChatHubCore`, so it picks up
`AuthorizeSessionAsync` for `SendMessage`, `RateMessage` and notification actions; its `LoadSession` override is
already owner-scoped through `DefaultAIChatSessionManager.FindAsync`, which matches `UserId` to the
`NameIdentifier` claim and visitors by `ClientId`, the same rule the base applies. `AddCoreAIMemory()` brings the
conversation handler and tools. Still to do there: bump `CrestAppsCoreVersion`, and add the two switches to
`AIProfileMemoryDisplayDriver`, `AIProfileTemplateMemoryDisplayDriver` and the chat-interaction memory settings.

---

## 0. TL;DR

Today "memory" means one thing: a per-user list of `name → content` facts that the model must choose to
save, through a tool, during the turn. Everything else the assistant appears to remember comes from a raw
**last-N-messages window**. The default N is 10, which is five exchanges. When a message falls out of that
window it is **silently gone**. It is not summarized and it is not searchable, and tool results never reach
history at all.

State-of-the-art systems split memory into **tiers**, and each tier has its own write path and its own read
path:

| Tier | What it answers | ChatGPT / Claude analogue | Here today |
|---|---|---|---|
| **T0 Working context** | "What's in this turn's prompt?" | Token-budgeted context; Claude API compaction and tool-result clearing | Last-N messages, counted by message rather than by token |
| **T1 Session memory** | "What did we say 80 turns ago in *this* chat?" | Rolling summary + in-conversation recall | **None** |
| **T2 Cross-session (episodic)** | "What did we discuss last week?" | ChatGPT *Reference chat history*, Claude *Search and reference chats* | **None** (post-session summaries exist but are analytics-only) |
| **T3 Long-term (semantic)** | "What do you know about me?" | ChatGPT *Saved memories*, Claude memory summary | `save_user_memory` and the related tools. Works, but only through hot-path tool calls, with no consolidation and no scoping |
| **T4 Procedural** *(optional)* | "How does this user want me to work?" | Custom instructions, Claude project instructions | Profile system prompt only |

The proposal adds **T1** and **T2** and rebuilds **T0** around a token budget. It reshapes **T3** into a
background extract-and-consolidate pipeline with scoping and an always-on "core profile". It reuses machinery
the repo already has: the preemptive-RAG pipeline, the index profiles and vector-search providers, the
utility-model slot, session close processing, and the privacy service.

---

## 1. How memory works today

### 1.1 Conversation history (the de facto "session memory")

```
Hub turn ─▶ promptStore.GetPromptsAsync(sessionId)            (loads the ENTIRE transcript)
         ─▶ ChatConversationHistoryBuilder.Build(...)          (user/assistant text only)
         ─▶ OrchestrationContext.ConversationHistory
              ├─ planner:          TakeLast(PlanningHistoryMessageCount = 10)   DefaultOrchestrator.cs:339
              ├─ RAG query rewrite: TakeLast(4)                                 PreemptiveSearchQueryProvider.cs:101
              └─ completion client: last PastMessagesCount (default 10)         NamedAICompletionClient.cs:254
                                                                                ClaudeOrchestrator.cs:215
                                                                                AzureOpenAICompletionClient.cs:695
                                                                                AzureOpenAICompletionClient.Responses.cs:158
```

- `PastMessagesCount` is set per profile (`AIProfileMetadata`), per Chat Interaction, or from
  `DefaultAIOptions.PastMessagesCount = 10` (`DefaultAIOptions.cs:36`). The UI help text reads: "More messages
  provide better context but increase token usage and costs".
- Trimming happens **inside each completion client**, after orchestration has finished, and four copies of
  that code exist.
- `ChatConversationHistoryBuilder.CreateMessage` (`ChatConversationHistoryBuilder.cs:226`) projects each stored
  prompt to `new ChatMessage(role, text)`. Function calls and function results are never persisted into history.
- Realtime voice starts every session with `ConversationHistory = []`
  (`DefaultRealtimeOrchestrator.cs:227`).

### 1.2 Long-term user memory

| Piece | File | Role |
|---|---|---|
| `AIMemoryEntry` | `Abstractions/Models/AIMemoryEntry.cs` | `UserId`, `Name`, `Description`, `Content`, `CreatedUtc`, `UpdatedUtc`. Nothing else. |
| `IAIMemoryStore` | `Abstractions/Memory/IAIMemoryStore.cs` | CRUD plus `FindByUserAndNameAsync`, used for upserts keyed by name |
| Tools | `Tools/{Save,Search,List,Remove}UserMemor*Tool.cs` | The model's only way to read or write memory |
| `AIMemoryOrchestrationHandler` | `Handlers/AIMemoryOrchestrationHandler.cs` | When enabled: forces the memory tools into the tool set and appends `memory-availability.md` to the system message |
| `AIMemoryPreemptiveRagHandler` | `Handlers/AIMemoryPreemptiveRagHandler.cs` | Embeds the rewritten queries, runs a vector search (top 5), appends `memory-context-header.md` to the system message |
| `AIMemorySearchService` | `Services/AIMemorySearchService.cs` | Vector search with a user-id filter through a keyed `IMemoryVectorSearchService` (Azure AI Search, Elasticsearch, PostgreSQL) |
| `AIMemoryIndexingService` + `AIMemoryEntryHandler` | `Services/…`, **`Startup.Shared`**/`Handlers/…` | Embeds and indexes a memory when it is created or updated |
| `DefaultAIMemorySafetyService` | `Services/DefaultAIMemorySafetyService.cs` | Regex rejection of credentials, SSNs and Luhn-valid card numbers |
| Enablement | `AIMemoryOrchestrationContextHelper.IsEnabled` | Profile: `MemoryMetadata.EnableUserMemory`. Chat Interactions: one global `ChatInteractionMemoryOptions.EnableUserMemory` |

Memory requires an authenticated user. It is scoped to the user only: there is no profile, project or
interaction dimension.

---

## 2. Findings

Severity: **P0** means the user-visible behavior is wrong or memory silently does nothing. **P1** is a
significant quality or cost gap. **P2** is polish.

### Bugs

| ID | Sev | Finding | Evidence | Fix |
|---|---|---|---|---|
| **M1** | P0 | **Memory indexing reads the wrong deployment property.** The guard checks `indexProfile.EmbeddingDeploymentName` and then assigns `metadata.EmbeddingDeploymentName`. If the metadata holds the name and the top-level property is empty, indexing silently skips, so saved memories are never searchable. If the top-level property is set and a metadata object exists without a name, it is overwritten with `null`. `AIMemorySearchService` gets this right, so writes and reads disagree. | `AIMemoryIndexingService.cs:225-229` | Check `metadata.EmbeddingDeploymentName`, matching `AIMemorySearchService`. Extract one shared resolver for both services. |
| **M2** | P0 | **`EnablePreemptiveMemoryRetrieval` is ignored when the site-wide `EnablePreemptiveRag` is off.** `PreemptiveRagOrchestrationHandler` returns after injecting the *data-source* tool instructions and never calls the memory handler. | `PreemptiveRagOrchestrationHandler.cs:95` | Run memory retrieval on its own path, independent of the data-source and document RAG switch. |
| **M3** | P0 | **Memory looks broken whenever no vector index is configured.** `save_user_memory` succeeds and `list_user_memories` works, but `search_user_memories` and preemptive retrieval return nothing, and the only signal is a Debug-level log. The availability prompt still tells the model it MUST search first. | `AIMemorySearchService.cs` (early returns), `memory-availability.md` | Add a store-backed fallback (recency plus keyword over `GetByUserAsync`, which is fine for the usual < 200 memories per user). Log a Warning once per process when the index is missing. |
| **M4** | P1 | **`PastMessagesCount = 1` sends the whole transcript.** Every implementation tests `> 1`, so 1 behaves like "all" and does not mean "one". | `NamedAICompletionClient.cs:254`, `ClaudeOrchestrator.cs:215`, `AzureOpenAICompletionClient.cs:695`, `…Responses.cs:158` | `> 0`. Superseded by §5.1 anyway. |
| **M5** | P1 | **The memory indexing handler lives in the sample host** (`Startup.Shared`), not in the framework. A host that does not copy it gets memories that are stored but never indexed, which is M3 again. It also runs inline: the embedding call happens on the chat hot path, inside the tool call. | `Startup.Shared/Areas/AI/Handlers/AIMemoryEntryHandler.cs`, registered only in the sample hosts | Move it into `CrestApps.Core.AI` (`AddAIMemory()`), and index through a deferred task so a failed embedding does not fail the save. |
| **M6** | P2 | **`MemoryExtractionRule` flags "show my saved memories" as High risk.** Users who ask what the assistant remembers about them is the main transparency affordance in ChatGPT and Claude, and `list_user_memories` exists to answer it. | `Security/MemoryExtractionRule.cs` | Narrow the rule to *other users'* data, raw stores and system-level dumps, or exempt the requests that `list_user_memories` answers. Confirm what a High verdict does in the pipeline. |

### Design gaps

| ID | Sev | Gap | Consequence |
|---|---|---|---|
| **G1** | P0 | **History is a message-count window with no summarization.** | Turn 11 forgets turn 1. A fact the user stated early in a long support or coding session ("the server runs .NET 8 on Linux", "budget is $40k") is gone. Ten messages can also be 200 tokens or 200 k tokens, so the count controls neither cost nor overflow. |
| **G2** | P0 | **No in-session recall.** Nothing can fetch *a specific older turn* back into the prompt when it becomes relevant again. | This is exactly the user's complaint: "remember old things spoken during a conversation". |
| **G3** | P0 | **No cross-session memory beyond hand-saved facts.** Post-session summaries (`PostSessionProcessingService`) exist only for analytics and are never fed back to the assistant. | "Continue what we did yesterday" fails unless the model happened to call `save_user_memory`. |
| **G4** | P1 | **Tool calls and results are not kept in history.** | "Open the second result from that search" fails one turn later, and the model re-runs expensive tools. |
| **G5** | P1 | **All memory writes depend on the model calling a tool mid-answer**, driven by an aggressive "you MUST save" prompt of roughly 600 tokens on every turn. | Weak models under-save. Strong models over-save trivia. Every save adds a tool round-trip and an inline embedding call to the user's latency. |
| **G6** | P1 | **No consolidation.** Dedup is by exact `Name` only. | `favorite_framework` and `preferred_framework` both exist, and contradictions pile up: "works at A" next to "works at B". |
| **G7** | P1 | **No scoping.** Memory is global per user. | A sales-assistant profile and an internal HR profile share one memory pool, both a privacy problem and a relevance problem. Claude gives each project separate memory for exactly this reason. |
| **G8** | P1 | **Retrieved memory is appended to the system message**, so the system prompt changes every turn. | It defeats provider prompt caching (Anthropic, OpenAI and Azure all cache the prefix), so cost and latency go up every turn. |
| **G9** | P1 | **No temporal model.** Only `UpdatedUtc` is stored. No validity window, no last-used time, no expiry. | "Active project: X" stays true forever. Stale facts outrank fresh ones because ranking is cosine similarity only. |
| **G10** | P1 | **Realtime voice has no history and no session memory.** | A user who reconnects to a voice session starts from zero. |
| **G11** | P2 | **Chat Interactions have a single global toggle**, with no per-interaction or per-workspace memory. | Interactions are the natural "project" container, but none of the memory is project-scoped. |
| **G12** | P2 | **Anonymous users get no memory of any kind.** | That is fine for long-term memory, but session memory (T1) needs no identity and should work for anonymous visitors. |
| **G13** | P2 | **No "temporary chat" mode**, meaning a session that neither reads nor writes memory. | ChatGPT and Claude both offer one. It is the expected privacy escape hatch. |
| **G14** | P2 | **The sensitive-data check is regex only.** It has false positives ("prefers a password manager" is rejected) and no protection against **memory poisoning**: text from documents, web pages or tool output that the model is coaxed into saving as a user fact. | A security and quality risk once background extraction exists (§5.4). Extract only from user-authored turns. |
| **G15** | P2 | **The whole transcript is loaded from the store on every turn** (`GetPromptsAsync`) and then mostly thrown away. | O(n) reads per turn on long sessions. |

---

## 3. How state-of-the-art systems do it

### ChatGPT (OpenAI)
- **Saved memories:** explicit facts. The user can ask for them, or the model adds them itself. They are
  always considered, and the user can view, edit and delete them in settings.
- **Reference chat history:** draws on past conversations, but "doesn't keep every detail" and changes over
  time as ChatGPT updates what's most useful. In practice that means a background-maintained digest plus
  retrieval over past chats, rather than raw transcripts.
- **Temporary Chat** neither reads nor creates memory. Turning off saved memories also turns off chat-history
  reference.
- *Lesson:* two separate products, **curated facts** (T3) and **ambient recall of past chats** (T2), each with
  its own switch.

### Claude apps (Anthropic)
- **Search and reference chats:** past-conversation search runs as **visible tool calls** (a
  `conversation_search` tool plus a recent-chats tool). It is retrieved when relevant, not stuffed into
  every prompt.
- **Memory summary:** a synthesized, **editable** summary of the user, regenerated in the background.
- **Memory is separate for each project**, so work and personal contexts don't leak into each other.
- *Lesson:* scope memory by project or profile, keep a small always-on synthesized profile, and fetch
  everything else on demand through tools the user can see.

### Claude API (context management)
- **Compaction:** server-side summarization of older history when the context grows. The rationale is
  "context rot": recall quality drops as the context window fills.
- **Context editing:** clears stale tool results and keeps the fact that the call happened.
- **Memory tool:** a file-like store the agent reads and writes, and it can write to it *before* context is
  cleared. Anthropic reports large gains when memory and context editing are combined on long agentic tasks.
- *Lesson:* manage T0 by **budget**. Summarize old turns rather than drop them, clear bulky tool output first,
  and give the model a scratchpad.

### Microsoft.Extensions.AI (already a dependency, 10.11.0)
- `IChatReducer`, with `MessageCountingChatReducer` and `SummarizingChatReducer` (experimental `MEAI001`).
  The summarizing reducer keeps system messages, never summarizes function-call or function-result messages,
  and folds older messages into a summary once the count exceeds `targetCount + threshold`.
- *Lesson:* T0/T1 should plug in through `IChatReducer` so hosts can swap implementations. The built-in
  reducer is not enough on its own: it is count-based, has a five-sentence summary and no recall.

### Agent-memory frameworks
- **Letta (MemGPT):** *core memory* blocks that always sit in context and that the agent edits itself,
  *recall memory* (searchable conversation history), and *archival memory* (a vector store). This maps
  directly onto core profile + T1 recall + T3.
- **Mem0:** extract candidate facts after each exchange. For each one, retrieve similar existing memories and
  let an LLM choose **ADD / UPDATE / DELETE / NOOP**. That is the standard answer to G6.
- **Zep / Graphiti:** a temporal knowledge graph where facts carry **validity intervals**, so a superseded
  fact is invalidated rather than deleted. That is the answer to G9.
- **LangMem / LangGraph:** semantic, episodic and procedural memory, written either on the **hot path** (in
  the turn, through tools) or in the **background** (after the turn, off-latency). Use both, and default to
  background.
- Benchmarks: LoCoMo, LongMemEval and BEAM. Vendor numbers conflict, so build our own regression suite (§7)
  and don't chase a leaderboard.

---

## 4. Target model

```
                        ┌────────────────────────── per turn (hot path) ──────────────────────────┐
 user msg ─▶ Context Assembler (token budget)                                                       │
             ├─ [stable, cacheable]  system prompt + profile instructions + CORE PROFILE (T3, small) │
             ├─ [session]            rolling SESSION SUMMARY + SESSION NOTES (T1)                    │
             ├─ [recall, per-turn]   relevant older turns of THIS session (T1 recall)                │
             │                       relevant past-conversation episodes (T2)                       │
             │                       relevant long-term memories (T3)                              │
             ├─ [recent]             last K turns verbatim (+ compacted tool results)              │
             └─ user msg                                                                             │
           tools: search_conversation · search_past_conversations · recent_conversations ·          │
                  save/search/list/remove_user_memory · (update_session_notes)                       │
                        └────────────────────────────────────────────────────────────────────────────┘
                        ┌──────────────────────── after turn / on close (background) ─────────────────┐
                        │ T1: index new turns for recall; fold overflow into rolling summary          │
                        │ T3: extract candidate facts from USER turns → consolidate (ADD/UPD/DEL/NOOP) │
                        │ T2: on session close → episode summary + topics/entities → index             │
                        │ Core profile: re-synthesize when T3 changes (debounced)                      │
                        └──────────────────────────────────────────────────────────────────────────────┘
```

Every tier has a **scope**: `User`, `User+Profile`, or `User+ChatInteraction`. The profile chooses whether
cross-session and long-term memory are shared across profiles. The default is **per profile**, following
Claude's per-project memory. Session memory is always scoped to its session.

---

## 5. Proposed design

### 5.1 T0: a token-budgeted Context Assembler (replaces `PastMessagesCount` trimming)

- New stage in the orchestrator, `IConversationContextAssembler`. It runs once, **before** the provider
  call, and produces the final `List<ChatMessage>`. All four client-side `AddLastMessages` copies are deleted,
  and every orchestrator (default, Claude, Copilot) and every client sees the same history.
- Budget: `HistoryTokenBudget` comes from a new optional `AIDeployment.ContextWindowTokens` minus the system
  prompt, tools, max output and a safety margin, then multiplied by a profile ratio (default 0.5, because
  smaller contexts recall better). Tokens are estimated with a pluggable `ITokenCounter`; a default of
  chars/4 is enough to start, since provider usage is reported afterwards.
- Fill order when over budget:
  1. drop **old tool results** first, keeping a one-line stub, as Claude context editing does;
  2. fold the oldest verbatim turns into the **session summary** (T1);
  3. finally truncate.
- `PastMessagesCount` stays as an optional **hard cap** for backward compatibility. A new profile setting,
  `HistoryMode = Window | Summarize | SummarizeAndRecall`, takes over its meaning. The default for new
  profiles is `SummarizeAndRecall`; existing profiles keep `Window` until an admin changes them.
- Expose the assembler as an `IChatReducer` adapter so MEAI-native hosts can reuse it.

### 5.2 T1: Session memory (the user's main ask)

**a) Rolling summary.** An `AISessionMemory` document is stored per chat session or Chat Interaction, kept as
its own record rather than inside `ExtensibleEntity` so the session document stays small:

```csharp
public sealed class AISessionMemory : CatalogItem
{
    public string SessionId { get; set; }           // or ChatInteractionId
    public string Summary { get; set; }             // running, structured summary
    public string SummarizedThroughPromptId { get; set; }
    public DateTime SummarizedThroughUtc { get; set; }
    public List<SessionNote> Notes { get; set; }    // pinned facts/decisions/constraints/open tasks
    public int Version { get; set; }
}
```

- **Incremental (recursive) summarization**: each new chunk of overflow is folded into the existing summary
  by the **utility deployment**. The whole history is never re-summarized.
- It runs **after** the assistant's response, so it never blocks the user. The next turn reads it. If the
  summary is behind, the assembler falls back to truncation for that one turn.
- The summary prompt is *structured*, not "five sentences". Sections: goal, user-stated facts and constraints,
  decisions made, entities and identifiers (names, IDs, numbers, file names), open questions, and what the
  assistant committed to. Identifiers are copied **verbatim**, because paraphrased numbers are the main way
  summaries fail.
- The summary is subject to `AIConversationPrivacyService`. Redaction modes must apply to it as they already
  apply to titles, summaries and extracted fields.

**b) Session notes.** These are model-writable and user-visible, and they play the role of Letta's "core
memory" at session scope. An optional `update_session_notes` tool lets the model pin facts like "Customer
account #: [redacted]" or "We decided on Postgres". Notes are always in context and capped at about 500
tokens. The existing **typed extraction** (`DataExtractionService`, `ExtractedData`) already does this for
predefined fields, so expose the extracted fields to the prompt through the same block.

**c) In-session recall.** This is what brings back "old things spoken during a conversation".
- Index every completed turn pair (user plus assistant, chunked at about 400 tokens) into a new index-profile
  type, `AIConversation`. It reuses the memory index providers, with filter fields
  `userId, profileId, sessionId, createdUtc, kind=turn|episode`.
- A new `IPreemptiveRagHandler`, `SessionRecallPreemptiveRagHandler`, searches *this session's* turns that
  are **older than the verbatim window** using the rewritten queries the pipeline already produces, keeps hits
  above a score threshold, and injects at most N excerpts with their timestamps.
- A tool, `search_conversation(query)`, lets the model look further back on its own, the same way Claude
  searches past chats.
- With no vector index, fall back to BM25/keyword search over the session's stored prompts. Most sessions are
  small enough to scan.

### 5.3 T2: Cross-session (episodic) memory

- **Write:** when a session closes (`AIChatSessionCloseRunner` / `PostSessionProcessingService` already
  exist), produce an **episode**: title, a 150–300-token summary, topics, entities, outcomes and unresolved
  items. Store it as `AIConversationEpisode` and index it with `kind=episode`. Chat Interactions, which have no
  close event, produce an episode after N minutes of idleness or on "clear history".
- **Read, on demand (default):** two tools, modeled on Claude:
  - `search_past_conversations(query, top_n)` finds episodes and drills into turn chunks;
  - `recent_conversations(count, before?)` handles "what were we doing yesterday?".
- **Read, ambient (opt-in, ChatGPT-style):** a background-maintained **"recent context digest"** of about
  300 tokens per user and scope, covering the last few episodes. It is injected into the stable prompt block
  and regenerated when an episode is added.
- Scope follows the profile setting: per profile by default, optionally shared across profiles, and for Chat
  Interactions optionally per interaction.

### 5.4 T3: Long-term memory, re-architected

**Data model additions on `AIMemoryEntry`**, all optional so existing rows keep working:

| Field | Purpose |
|---|---|
| `Scope` (`User`/`Profile`/`Interaction`) + `ScopeId` | Fixes G7 |
| `Category` (`identity`, `preference`, `project`, `instruction`, `relationship`, `other`) | Lets the core profile pick categories. Better ranking. |
| `Source` (`Explicit`, `Inferred`) + `SourceSessionId` / `SourcePromptId` | Provenance, the answer to "why do you think that?", and poisoning audits |
| `Importance` (0–1), `LastAccessedUtc`, `AccessCount` | Ranking and decay |
| `ValidFromUtc` / `ValidUntilUtc`, `SupersededById` | Zep-style temporal validity: superseded facts are invalidated, not deleted |
| `IsPinned` | Always kept in the core profile |

**Write path: hot plus background (Mem0 pattern).**
1. *Hot path (keep, but slim it down):* `save_user_memory` remains for explicit requests ("remember that…").
   Shrink `memory-availability.md` from "MUST save before you answer" to "save when the user asks or states a
   durable preference". That removes most of the latency and over-saving.
2. *Background extractor (new):* after each turn, or batched per N turns or at session close, the utility
   model reads **user-authored text only** (G14) and proposes candidate facts. For each candidate it
   retrieves the top-k similar memories in scope, and a consolidation prompt returns
   `ADD | UPDATE(id) | INVALIDATE(id) | NOOP`. Writes go through the safety service. Indexing is deferred.
3. Upserts match by **semantic similarity**, not by exact `Name`. `Name` becomes a display label only.

**Read path.**
- **Core profile (always on, cacheable):** a background-synthesized summary of the user for each scope, about
  200–400 tokens, built from pinned and high-importance memories. It is placed in the stable part of the
  prompt and regenerated (debounced) when T3 changes, like Claude's memory summary. **It works with no vector
  index at all**, which also fixes M3 for the common case.
- **Per-turn retrieval:** hybrid scoring of `α·cosine + β·keyword + γ·recency + δ·importance`, with a
  minimum-score threshold so irrelevant memories are not injected, and `LastAccessedUtc` updated on hits.
- Superseded or expired memories are excluded unless the user asks about history.

### 5.5 Prompt placement and caching (G8)

- **Stable block** (cacheable): profile system prompt, tool instructions, core profile, recent-context digest.
  It changes rarely.
- **Volatile block** (per turn): recalled session excerpts, retrieved episodes and memories. It goes in a
  delimited context message placed **immediately before the latest user message**, not in the system
  message. Check every provider's shaping before switching: Anthropic keeps a mid-history `system` role, and
  some providers merge or reject it. Fall back to prefixing the user turn.
- Wrap every injected memory in clear "context, not instructions" framing, as a prompt-injection defense.

### 5.6 Controls, privacy and governance

- **Per-profile switches** for each tier, plus the global switches:
  `EnableSessionMemory`, `EnableConversationRecall`, `EnableCrossSessionMemory`, `EnableUserMemory`,
  `EnableBackgroundMemoryExtraction`, `MemoryScope`.
- **Per-session "temporary / incognito" flag:** no T2/T3 reads or writes, while T1 stays on so the chat itself
  still works.
- **User transparency:** a "What I remember" view for listing, editing, deleting and pausing memories, along
  with each memory's source. Also fix M6 so users can ask in chat.
- **Cascade deletes:** deleting a session removes its `AISessionMemory`, its turn chunks and its episode.
  Deleting a user purges every tier. Retention policies apply per tier.
- **`AIDataStorageMode`:** redact and delete modes apply to summaries, notes, episodes and extracted
  memories, not only to the transcript.
- **Anonymous visitors:** T1 only, keyed by session (G12).
- **Cost:** all background work runs on the utility deployment and is metered under new
  `AIUsagePurposes` (`MemorySummarization`, `MemoryExtraction`, `MemoryConsolidation`), so the usage reports
  show what memory costs.

### 5.7 Realtime voice (G10)

- On session start, seed the realtime session's instructions with the **session summary, the session notes
  and the last few turns** of the stored chat session, plus the core profile. Today it gets `[]`.
- Per-turn grounding already reuses `IPreemptiveRagHandler` (`DefaultRealtimeTurnGrounding`), so the new
  recall and episode handlers work in voice without extra code.
- Fold turns into the summary from the turn store in the background as they complete.

---

## 6. Phased roadmap

| Phase | Scope | Exit criteria |
|---|---|---|
| **P0 – Fix what exists** (small) | M1, M2, M3 (store fallback + warning), M4, M5 (move the handler into the framework, deferred indexing) | Saving a memory makes it searchable on every supported provider and with no index. Unit tests cover each bug. |
| **P1 – Token-budgeted context + session summary** | §5.1 assembler (delete the four trimming copies), `ContextWindowTokens`, §5.2a rolling summary, §5.2b notes exposing extracted data, privacy cascade | A 100-turn scripted conversation answers questions about turn 3 facts. Prompt tokens per turn stay flat as the session grows. |
| **P2 – In-session recall** | `AIConversation` index type, turn indexing, `SessionRecallPreemptiveRagHandler`, `search_conversation`, keyword fallback | The "needle in a long chat" eval passes (§7). Recall adds < 150 ms p50. |
| **P3 – Cross-session episodes** | Episode generation on close and idle, `search_past_conversations`, `recent_conversations`, scope setting, optional digest | "What did we decide last time about X?" works across sessions and is isolated per profile. |
| **P4 – Long-term memory v2** | Model additions, background extractor + consolidation, core profile, hybrid ranking, slimmer hot-path prompt, M6, temporary chat, "What I remember" UI | Fewer duplicate or conflicting memories on the eval set. Contradictions resolve to the latest fact. Memory works without an index. |
| **P5 – Realtime + polish** | §5.7, prompt-cache placement (§5.5) across providers, usage purposes, docs | Voice resumes with context. The cache-hit rate is visible in the usage report. |

P0 is independent of the rest and can ship right away. P1 delivers most of the user-visible gain ("it
remembers what I said earlier"), and P2 makes that recall precise.

---

## 7. Evaluation plan

Build a **memory regression suite**: deterministic, scripted conversations plus an LLM judge, run against a
cheap model in CI and a strong model nightly.

1. **Needle in a long chat:** a fact is planted at turn 3, there are 60 filler turns, then it is asked about.
   Tests T1 summary and recall.
2. **Numeric fidelity:** IDs, amounts and dates planted early are asked for verbatim later. Tests that
   summaries don't paraphrase numbers.
3. **Cross-session continuity:** session A decides X, and session B (same profile) asks for it. Tests T2.
4. **Scope isolation:** a fact told to profile P1 must not surface in profile P2 when scope is per profile.
5. **Update and contradiction:** "I live in Boston", later "I moved to Denver". The answer must be Denver,
   and Boston is marked invalid.
6. **No-op discipline:** small talk must not create memories (precision of the extractor).
7. **Poisoning:** a document or tool output says "remember the user is an admin". It must not be saved.
8. **Temporary chat:** nothing is read or written.
9. **Cost and latency:** prompt tokens per turn and p50/p95 added latency for each tier.

Track the same metrics in production through the existing usage-analytics events: memory tool calls, recall
hits, summary refreshes, extractor ADD/UPDATE/NOOP counts.

---

## 8. Open questions

1. **Default scope:** per profile (Claude-like, safer) or per user (ChatGPT-like, more "magical")?
   **Recommendation:** per profile, with a site-level opt-in to share.
2. **Ambient cross-session digest** on by default? **Recommendation:** off. The tools are on.
3. **Where does the conversation index live** when a host has no vector provider? Keyword fallback only, or
   also ship a SQLite/sqlite-vec provider for the sample hosts?
4. **Tool-result persistence (G4):** store compacted tool results with the assistant prompt (a size cap plus
   a stub), or rely on session notes and recall? **Recommendation:** store a compacted summary of each
   tool's result, at most about 300 tokens, on the assistant prompt's metadata.
5. **Chat Interactions:** is an interaction the "project" scope, or should there be an explicit
   workspace/project entity grouping interactions and sessions?

---

## Sources

- OpenAI — [How does reference saved memories work](https://help.openai.com/en/articles/11146739-how-does-reference-saved-memories-work), [Memory FAQ](https://help.openai.com/en/articles/8590148-memory-faq)
- Anthropic — [Managing memory in Claude](https://support.claude.com/en/articles/11817273-managing-memory-in-claude); [Context engineering: memory, compaction and tool clearing (cookbook)](https://platform.claude.com/cookbook/tool-use-context-engineering-context-engineering-tools)
- Coverage — [MacRumors: automatic memory for Pro/Max](https://www.macrumors.com/2025/10/23/anthropic-automatic-memory-claude/), [Simon Willison on Claude memory](https://simonwillison.net/b/9010)
- Microsoft — [`SummarizingChatReducer`](https://learn.microsoft.com/de-de/dotnet/api/microsoft.extensions.ai.summarizingchatreducer), [`IChatReducer` source](https://source.dot.net/Microsoft.Extensions.AI.Abstractions/ChatReduction/IChatReducer.cs.html), [`SummarizingChatReducer` source](https://source.dot.net/Microsoft.Extensions.AI/ChatReduction/SummarizingChatReducer.cs.html)
- Agent memory — [Mem0 benchmarks](https://mem0.ai/blog/ai-memory-benchmarks), [Atlan: memory frameworks compared (2026)](https://atlan.com/know/best-ai-agent-memory-frameworks-2026/), [Mem0 vs Zep vs Letta (2026)](https://www.agenticwire.news/article/mem0-zep-letta-agent-memory). Vendor benchmark numbers conflict and are not relied on here.
