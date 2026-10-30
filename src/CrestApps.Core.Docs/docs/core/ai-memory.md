---
sidebar_label: AI Memory
sidebar_position: 13
title: AI Memory
description: Long-term memory services for user-scoped facts, semantic retrieval, and memory-aware orchestration.
---

# AI Memory

`CrestApps.Core` includes reusable memory services for applications that want an AI assistant to remember durable user facts across sessions.

## What the framework provides

`AddCoreAIMemory()` adds the shared runtime behavior for:

- memory tool registration
- safety validation for memory writes
- semantic memory search orchestration
- preemptive memory retrieval during orchestration
- shared indexing and search helpers

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddAIMemory(memory => memory
            .AddEntityCoreStores()
        )
        .AddOpenAI()
    )
    .AddEntityCoreSqliteDataStore("Data Source=app.db")
);
```

## What your host must provide

The framework does not assume a single persistence model. A host application is responsible for wiring the storage and search pieces that match its runtime:

- an `IAIMemoryStore` implementation for durable memory entries
- a persistent `ISearchIndexProfileStore` implementation for index profile lookup when you want saved index profiles (registered via `.AddIndexingServices(indexing => indexing.AddEntityCoreStores())` or `.AddYesSqlStores()`). `AddCoreAIServices()` already supplies a null fallback store so hosts can start before a persistent store is added.
- optionally, one or more keyed `IMemoryVectorSearchService` implementations and an AI Memory index profile named by `AIMemoryOptions.IndexProfileName`. Without them, memory still works: search ranks the user's stored memories by the terms they share with the query (see [Search without an index](#search-without-an-index))
- options such as `AIMemoryOptions`, `GeneralAIOptions`, and `ChatInteractionMemoryOptions`

`AddAIMemory()` also registers the catalog handler that keeps the index in step with saved memories. Each saved or deleted memory is queued and written to the index by a hosted background service, so saving never waits on an embedding call. A host that indexes memories with its own `ICatalogEntryHandler<AIMemoryEntry>` calls `services.AddCoreAIMemory()` alone instead of `AddAIMemory()`, so memories are not indexed twice; `services.AddCoreAIMemoryIndexing()` adds the framework's indexing on its own.

Register stores directly on the AI memory builder:

**Entity Framework Core (via builder):**

```csharp
.AddAIMemory(memory => memory
    .AddEntityCoreStores()
)
```

**YesSql (via builder):**

```csharp
.AddAIMemory(memory => memory
    .AddYesSqlStores()
)
```

Both register the `IAIMemoryStore` implementation. See [Data Storage](data-storage.md) for the full per-feature store reference.

## Core concepts

### Memory entries

A memory entry is a durable user-scoped fact:

| Field | Purpose |
| --- | --- |
| `UserId` | Identifies the owner of the memory |
| `Name` | Stable key such as `preferred-language` |
| `Description` | Semantic summary used to improve retrieval quality |
| `Content` | The value to retain for later recall |
| `CreatedUtc` / `UpdatedUtc` | Lifecycle timestamps |

### Safety validation

Before a memory is stored, `IAIMemorySafetyService` can reject obviously sensitive data such as credentials, connection strings, SSNs, or payment card numbers. The framework ships with the validation pipeline; hosts decide how they surface validation failures.

### User scoping

Memory tools operate on the current authenticated user. The framework resolves identity from orchestration scope or the current HTTP context so retrieval stays user-specific.

### Search without an index

`IAIMemorySearchService` searches the configured AI Memory index when there is a usable one. It falls back to the memory store when:

- no index profile is configured;
- the configured profile is missing, is not an AI Memory profile, has no vector-search provider, or has no embedding deployment it can resolve (logged as a warning once per profile name);
- the index holds nothing for the user, for example memories saved before the index was configured.

The fallback reads up to 200 of the user's memories and ranks them by the share of each query's terms that a memory's name, description and content contain, then by most recently updated. Memories that share no term are left out. It is less precise than vector search, but it means a saved memory can always be found again.

### Preemptive retrieval

When `GeneralAIOptions.EnablePreemptiveMemoryRetrieval` is on and memory is enabled for the profile or chat interaction, relevant memories are retrieved before the model answers and added to the system message. This follows its own switch: turning off site-wide preemptive RAG (`DefaultOrchestratorSettings.EnablePreemptiveRag`), which hands data-source and document retrieval to the search tools, does not turn memory retrieval off. A custom `IPreemptiveRagHandler` with its own switch opts into the same behavior by returning `true` from `RunsWhenPreemptiveRagIsDisabled`.

## Three kinds of memory

| Kind | What it remembers | Turned on by | Who it covers |
| --- | --- | --- | --- |
| **Session memory** | The earlier part of a long conversation | `MemoryMetadata.EnableSessionMemory` on a profile; `ChatInteractionMemoryOptions.EnableSessionMemory` (on by default) for chat interactions | The conversation itself; works for anonymous visitors |
| **Past conversations** | The user's earlier conversations with the same profile | `MemoryMetadata.EnablePastConversations` on a profile | The signed-in user only |
| **User memory** | Durable facts about the user | `MemoryMetadata.EnableUserMemory`; `ChatInteractionMemoryOptions.EnableUserMemory` | The signed-in user only |

In the sample hosts all three are switches on the profile editor's **Knowledge** tab, and the chat-interaction defaults are on the **Settings** page.

### Session memory: long conversations

Without it, a conversation longer than the profile's past-messages count simply loses its oldest messages: turn 11 of a conversation with a count of 10 no longer knows what was said in turn 1. With session memory on:

- **The messages that fall outside the window are summarized.** Once six or more have fallen out unsummarized, the utility deployment folds them into a running summary, which is stored on the `AIChatSession` or `ChatInteraction` as `AIConversationMemory`. The summary keeps names, figures and identifiers verbatim. Summarizing happens about every three turns, not every turn.
- **The messages not yet summarized are shown verbatim**, so nothing is lost between summaries.
- **Older turns that bear on the current message are recalled** word for word, matched by the terms they share with it, up to three turns.
- **The model can search the rest** with the `search_conversation` tool when tools are enabled.

All of it is added to the system message as an "Earlier in This Conversation" block. Session memory needs a positive past-messages count; with no count the whole conversation is already sent. When the history is cleared, the summary starts again. A failed summary keeps the previous one and the turn goes ahead.

`search_conversation` reads only the older messages the orchestration handler placed on the current `AIInvocationScope`, never a store, so it cannot reach another conversation.

### Past conversations

With past conversations on, the model gets two tools, `search_past_conversations` and `list_recent_conversations`, and instructions to use them when the user refers to an earlier conversation. They search the signed-in user's own conversations with the same profile, newest first, matching the title, the stored summary and the transcript. They are not offered to anonymous visitors.

Every conversation the tools read is checked against the signed-in user and the profile, not only filtered by the store query (`AIChatSessionQueryContext.UserId`), so a store that ignores the filter still leaks nothing; it logs a warning instead.

### Conversation ownership

`AIChatHubCore` checks `AuthorizeSessionAsync` before it loads, continues, rates or acts on a session by its id. The default allows only the signed-in user who started the session or, for a visitor's session, the same visitor. Someone else's id is reported as not found, and sending a message to it starts a new session of the caller's own. Override `AuthorizeSessionAsync` to let others in as well; the sample hosts let administrators open any conversation from the admin Sessions page.

### Privacy

The running summary repeats what the transcript says, so conversation privacy treats it the same way: it is included when personal details are looked for, redacted with the transcript, and removed when the transcript is stripped.

## Key contracts

| Contract | Purpose |
| --- | --- |
| `IAIMemoryStore` | CRUD and query access for persisted memory entries |
| `IAIMemorySearchService` | Shared semantic retrieval over memory entries |
| `IMemoryVectorSearchService` | Provider-specific vector search adapter |
| `IAIMemorySafetyService` | Validation for writes before they are stored |
| `IPreemptiveRagHandler` | Injects relevant memory context before the model responds |

## Built-in tools

When memory is enabled, the orchestration layer can expose these system tools:

| Tool | Purpose |
| --- | --- |
| `save_user_memory` | Create or update a durable memory |
| `search_user_memories` | Find relevant memories by semantic similarity |
| `list_user_memories` | Enumerate saved memories for the current user |
| `remove_user_memory` | Delete a saved memory by name |
| `search_conversation` | Search the earlier part of the current conversation (session memory) |
| `search_past_conversations` | Search the signed-in user's earlier conversations with the profile (past conversations) |
| `list_recent_conversations` | List the signed-in user's most recent earlier conversations with the profile (past conversations) |

The user-memory tools are intended for long-lived facts such as preferences, recurring projects, or roles, not for transient one-off chat state. The conversation tools are only offered when their feature is on and there is something for them to read.

## Typical flow

1. Register `AddCoreAIMemory()` with the rest of the AI runtime.
2. Provide the store, vector search, and option bindings for your host.
3. Enable memory-aware orchestration for the profiles or chat surfaces that should use it.
4. Let the orchestrator decide when to store, search, or inject memory context.

## Related guidance

- Pair memory with **[Orchestration Overview](../orchestration/index.md)** when you want automatic recall
- Pair memory with **[Data Sources](../data-sources/index.md)** when you also need document or index-backed RAG
