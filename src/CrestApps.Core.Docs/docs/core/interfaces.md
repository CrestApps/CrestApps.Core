---
sidebar_label: Interface Reference
title: Interface Reference
description: A practical guide to the main public interfaces exposed by CrestApps.Core.
---

# Interface Reference

This repository exposes many public interfaces across its abstraction packages. Rather than duplicating every signature in a long manually maintained page, this document maps the important contract groups to the packages and source folders that own them.

## Source packages

| Area | Source package |
| --- | --- |
| Core models and catalog contracts | `src\Abstractions\CrestApps.Core.Abstractions` |
| Infrastructure and indexing contracts | `src\Abstractions\CrestApps.Core.Infrastructure.Abstractions` |
| AI runtime contracts | `src\Abstractions\CrestApps.Core.AI.Abstractions` |
| Provider and protocol extensions | `src\Primitives\CrestApps.Core.AI.*` |

## Foundational contracts

Use these contracts when you are building reusable storage or management infrastructure:

| Interface family | Purpose |
| --- | --- |
| `IReadCatalog<T>`, `ICatalog<T>` | Query and mutate catalog-backed data |
| `IReadCatalogManager<T>`, `ICatalogManager<T>` | Validation and lifecycle handling over catalogs |
| `INamedCatalog<T>`, `ISourceCatalog<T>`, related managers | Name-based and source-based lookup patterns |
| `ICatalogEntryHandler<T>` | Hooks for create, update, and delete events |
| `INameAwareModel`, `IDisplayTextAwareModel`, `ISourceAwareModel` | Common model markers used across the framework |

## AI runtime contracts

These contracts define the provider-agnostic AI surface:

| Interface family | Purpose |
| --- | --- |
| `IAIClientFactory`, `IAIClientProvider` | Resolve provider-specific chat, embedding, image, and speech clients |
| `IAICompletionService`, `IAICompletionContextBuilder` | Direct completion flow and prompt/context assembly |
| `IOrchestrator`, `IOrchestratorResolver`, `IOrchestrationContextBuilder` | Agentic orchestration and execution loops |
| `IToolRegistry`, `IToolRegistryProvider`, `IAIToolAccessEvaluator` | Tool discovery, registration, and access control |
| `IAIProfileManager`, `IAIProfileStore`, `IAIDeploymentManager` | Profiles, deployments, and runtime configuration |

## Chat and response contracts

Use these when you are building interactive experiences:

| Interface family | Purpose |
| --- | --- |
| `IAIChatSessionManager`, `IAIChatSessionHandler` | Session persistence and lifecycle handling |
| `IChatInteractionSettingsHandler` | Chat-surface configuration and feature toggles |
| `IChatResponseHandler`, `IChatResponseHandlerResolver` | Deferred, streaming, or externalized response delivery |
| `IChatNotificationSender`, `IChatNotificationTransport` | Notification and relay workflows |
| `IExternalChatRelay*` interfaces | External relay integrations and transport-specific behavior |

## Memory, documents, and retrieval

These contracts support RAG-style applications:

| Interface family | Purpose |
| --- | --- |
| `IAIMemoryStore`, `IAIMemorySearchService`, `IMemoryVectorSearchService` | Long-term memory persistence and semantic retrieval |
| `IAIDataSourceStore`, `IDataSourceContentManager`, `IAIDataSourceIndexingService`, `IAIDataSourceIndexingQueue` | External data source registration, retrieval, and asynchronous synchronization |
| `ISearchIndexManager`, `ISearchDocumentManager`, `ISearchDocumentHandler`, `IVectorSearchService` | Search-index, vector-search, and post-write document event abstractions |
| `IDataSourceDocumentReader` | File and content ingestion by source type |
| `IIndexProfileHandler` | Index-profile validation, field definitions, and provider-specific schema hooks |

### Indexing and data-source extension reference

These are the primary extension points behind asynchronous data-source synchronization. For end-to-end flow details, see **[Data Sources](../data-sources/index.md#automatic-synchronization)**.

| Interface | Methods | Parameters |
| --- | --- | --- |
| `IAIDataSourceIndexingService` | `SyncAllAsync`, `SyncDataSourceAsync`, `SyncSourceDocumentsAsync` overloads, `RemoveSourceDocumentsAsync` overloads, `DeleteDataSourceDocumentsAsync` | `dataSource`: mapped AI data-source definition; `sourceIndexProfileName`: source profile name to scope partial sync/removal; `documentIds`: source document ids to process; `cancellationToken`: stops the operation. |
| `IAIDataSourceIndexingQueue` | `QueueSyncDataSourceAsync`, `QueueDeleteDataSourceAsync`, `QueueSyncSourceDocumentsAsync`, `QueueRemoveSourceDocumentsAsync` | `dataSource`: mapped AI data-source definition to enqueue; `sourceIndexProfileName`: source profile name that produced the event; `documentIds`: source ids to refresh or remove; `cancellationToken`: stops queue submission. |
| `ISearchDocumentHandler` | `DocumentsAddedOrUpdatedAsync`, `DocumentsDeletedAsync` | `profile`: source index profile that completed the write/delete; `documentIds`: successfully mutated source document ids; `cancellationToken`: stops follow-up work. |
| `IIndexProfileHandler` | `ValidateAsync`, `GetFieldsAsync`, `SynchronizedAsync`, `ResetAsync`, `DeletingAsync` | `indexProfile`: profile being validated, synchronized, reset, or deleted; `result`: validation result collector used by `ValidateAsync`; `cancellationToken`: stops provider-specific work. |

#### `IIndexProfileHandler`

Provider packages implement this contract to define schema, validation, and lifecycle behavior for search index profiles.

```csharp
public interface IIndexProfileHandler
{
    ValueTask ValidateAsync(
        SearchIndexProfile indexProfile,
        ValidationResultDetails result,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<SearchIndexField>> GetFieldsAsync(
        SearchIndexProfile indexProfile,
        CancellationToken cancellationToken = default);

    Task SynchronizedAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default);
    Task ResetAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default);
    Task DeletingAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default);
}
```

| Method | What it does | Parameters |
| --- | --- | --- |
| `ValidateAsync(indexProfile, result, cancellationToken)` | Validates provider-specific profile settings before the profile is saved or synchronized. | `indexProfile`: the profile being validated. `result`: the validation collector to populate. `cancellationToken`: stops validation work. |
| `GetFieldsAsync(indexProfile, cancellationToken)` | Returns the provider-specific search field definitions for the profile. | `indexProfile`: the profile whose schema is being built. `cancellationToken`: stops field generation. |
| `SynchronizedAsync(indexProfile, cancellationToken)` | Runs after the profile has been synchronized to the remote provider. | `indexProfile`: the profile that finished synchronization. `cancellationToken`: stops post-sync work. |
| `ResetAsync(indexProfile, cancellationToken)` | Clears provider-specific state before a rebuild or reprovisioning operation. | `indexProfile`: the profile being reset. `cancellationToken`: stops the reset. |
| `DeletingAsync(indexProfile, cancellationToken)` | Runs before a profile is deleted so provider-specific cleanup can happen first. | `indexProfile`: the profile being deleted. `cancellationToken`: stops delete preparation. |

## Provider and protocol extensions

Provider packages add focused extension points for specific transports and SDKs:

| Package | Examples |
| --- | --- |
| `CrestApps.Core.AI.OpenAI` | OpenAI client and completion configuration |
| `CrestApps.Core.AI.OpenAI.Azure` | Azure OpenAI integration contracts |
| `CrestApps.Core.AI.Mcp` | MCP client, server, prompt, and resource abstractions |
| `CrestApps.Core.AI.A2A` | A2A client and host support contracts |
| `CrestApps.Core.AI.Copilot` | Copilot-specific credential and orchestration contracts |

## How to use this page

1. Find the contract family you need.
2. Open the owning package in `src\Abstractions` or `src\Primitives`.
3. Use the source for the exact current signature.

For end-to-end usage patterns, pair this page with **[Core Overview](./index.md)**, **[Core Services](./core-services.md)**, and **[Orchestration Overview](../orchestration/index.md)**.
