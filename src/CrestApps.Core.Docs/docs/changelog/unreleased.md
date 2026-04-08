---
sidebar_label: Unreleased
sidebar_position: 1
title: Unreleased
description: Changes that are available in the current development branch and not yet included in a tagged release.
---

# Unreleased

## Bug fixes

- Fixed chat interaction persistence and upload stability in three places: the shared chat document minimal APIs now opt into `StoreCommitterEndpointFilter`, SignalR store commits now run through real hub options and resolve from the active hub scope, and the document readers now buffer uploaded files before handing them to PDF/Open XML parsers instead of reading directly from the ASP.NET upload stream. The shared chat UI also keeps the interaction ID attribute in sync for autosave operations. Attached files and chat settings such as RAG strictness and retrieved-document counts now persist correctly after reload instead of appearing to save only in the current request, and document uploads no longer terminate the host while parsing certain uploaded files.
- Fixed an orphaned-prompt bug in `EntityCoreAIChatSessionManager.NewAsync`: the initial chat prompt is now staged in the EF Core change tracker inside `NewAsync` and committed atomically when `SaveAsync` is called, so no prompt record can be persisted without a corresponding session record.
- Fixed `EntityCoreAIChatSessionManager.DeleteAsync` and `DeleteAllAsync` so that deleting a session also removes all associated prompt catalog records in the same `SaveChangesAsync` call, preventing orphaned prompt rows.
- Fixed the A2AConnection row in the model-types table: the catalog type column previously showed `Source` when the correct value is `Basic`.
- Fixed `ISearchIndexProfileStore`: removed the redundant `FindByNameAsync` shadow method (the interface already extends `INamedCatalog<SearchIndexProfile>`, which provides the method) and aligned the return type to `ValueTask<SearchIndexProfile>` across all implementations.

## Testing

- Strengthened the AI chat MVC test suite by expanding `MvcAIDocumentIndexingService`, `YesSqlAIChatSessionManager`, and `MvcAIChatDocumentEventHandler` coverage to exercise real branch behavior, argument guards, filtered indexing inputs, and queue/persistence side effects instead of relying on shallow mock-call assertions.

## Data storage

- Added `CrestApps.Core.Data.EntityCore`, a first-party Entity Framework Core store package that implements the shared CrestApps catalog/store surface and includes SQLite-friendly registration helpers plus schema initialization for local development.
- Updated the storage guidance so hosts can choose between the YesSql and EntityCore packages or plug in their own custom store implementation while keeping the same framework-facing abstractions.
- Removed repository-level `SaveChangesAsync()` from the shared catalog contracts so `ICatalog<T>` implementations stage writes and the host owns the YesSql unit-of-work boundary.
- Added a shared `AddCoreYesSqlDataStore(...)` bootstrap in `CrestApps.Core.Data.YesSql` so standalone hosts can reuse the same store/session initialization pattern without copying the MVC sample's setup code.
- Moved the shared AI chat session metrics YesSql index model/provider and schema helpers into `CrestApps.Core.Data.YesSql` so multiple hosts can reuse the same definitions while still applying host-specific schema options.
- Moved the remaining reusable AI-related YesSql index providers and sample-host index models into `CrestApps.Core.Data.YesSql`, and updated the shared metrics schema helpers so fresh installs create the full chat metrics table shape in one step.
- Reorganized the shared YesSql index assets under `Indexes/{Feature}` and expanded the shared schema-helper pattern so AI storage definitions stay grouped by feature instead of being scattered across the store project.
- Flattened the shared YesSql feature folders so index models, providers, and schema helpers now live directly under `Indexes/{Feature}` without an extra nested `Indexes` layer.
- Co-located each shared YesSql index type with its matching `IndexProvider` in the same file and aligned the shared store namespaces with the physical folder structure (`CrestApps.Core.Data.YesSql.Indexes.{Feature}`) so the reusable indexing surface is easier to navigate and maintain.
- Updated the MVC YesSql startup path to rewrite legacy stored `Document.Type` values from older `CrestApps.AI.*`, `CrestApps.Infrastructure.*`, and `CrestApps.Mvc.Web*` assemblies so existing local SQLite data keeps loading after the framework renames.
- Fixed the MVC YesSql startup path so the sample host no longer re-registers `IStore` while attaching index providers. The host now registers shared YesSql indexes during schema initialization, which restores normal MVC startup, HTTP binding, and log-file creation.
- Fixed the MVC Chat Interactions speech UX so typed prompts and microphone dictation no longer auto-play the assistant response. Automatic spoken playback now happens only for turns created while live conversation mode is active.
- Added `CrestAppsEntityDbContextSaveChangesMiddleware` as an opt-in middleware for custom stores that want to defer all `SaveChangesAsync` calls to the end of an HTTP request. The built-in CrestApps EntityCore stores commit per operation and do not use this middleware.
- Replaced the middleware-based YesSql unit-of-work pattern with `IStoreCommitter`, a lightweight interface that abstracts the flush signal for staged-write store implementations. `AddCoreYesSqlDataStore()` registers `YesSqlStoreCommitter` automatically; call `AddCrestAppsStoreCommitterFilter()` on your MVC builder and SignalR builder to enable automatic commits at the end of each request.
- Added `StoreCommitterActionFilter` (MVC), `StoreCommitterEndpointFilter` (Minimal API), and `StoreCommitterHubFilter` (SignalR) so hosts no longer need to add HTTP middleware for YesSql session flushing.
- Removed `CrestAppsEntityDbContextSaveChangesMiddleware` and `UseEntityCoreSaveChanges` from the EntityCore package (EF Core stores commit per-operation; a deferred middleware provides no benefit and can mask bugs).
- Refined the registration builder into a single `AddCrestAppsCore(builder => ...)` root entrypoint with higher-level suites such as `AddAISuite(...)` and `AddIndexingServices(...)`, so the framework can grow by capability area instead of accumulating a flat extension list.
- Standardized the public registration names around an `AddCrestApps...` pattern (for example `AddCoreAIOrchestration()`, `AddCoreAIDocumentProcessing()`, and `AddCoreAIOpenAI()`) while keeping compatibility wrappers for the older names.
