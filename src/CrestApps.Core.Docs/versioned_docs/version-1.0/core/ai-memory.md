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
- one or more keyed `IMemoryVectorSearchService` implementations
- options such as `AIMemoryOptions`, `GeneralAIOptions`, and `ChatInteractionMemoryOptions`

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

These tools are intended for long-lived facts such as preferences, recurring projects, or roles, not for transient one-off chat state.

## Typical flow

1. Register `AddCoreAIMemory()` with the rest of the AI runtime.
2. Provide the store, vector search, and option bindings for your host.
3. Enable memory-aware orchestration for the profiles or chat surfaces that should use it.
4. Let the orchestrator decide when to store, search, or inject memory context.

## Related guidance

- Pair memory with **[Orchestration Overview](../orchestration/index.md)** when you want automatic recall
- Pair memory with **[Data Sources](../data-sources/index.md)** when you also need document or index-backed RAG
