---
sidebar_label: Data Storage
sidebar_position: 12
title: Data Storage
description: Pluggable catalog pattern for persistent data storage with first-party YesSql and Entity Framework Core implementations.
---

# Data Storage

> A pluggable catalog pattern for CRUD operations on framework models, with first-party `CrestApps.Core.Data.YesSql` and `CrestApps.Core.Data.EntityCore` packages plus support for custom implementations.

## Quick Start

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    // YesSql + SQLite
    .AddYesSqlDataStore(configuration => configuration
        .UseSqLite("Data Source=app.db;Cache=Shared")
        .SetTablePrefix("CA_")));

builder.Services.AddYesSqlNamedSourceDocumentCatalog<AIProfile, AIProfileIndex>();

// Entity Framework Core + SQLite
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddEntityCoreSqliteDataStore("Data Source=app.db"));
builder.Services.AddEntityCoreStores();
```

`AddYesSqlDataStore(...)` on the root CrestApps builder maps to `AddCoreYesSqlDataStore(...)`, while `AddEntityCoreSqliteDataStore(...)` maps to `AddCoreEntityCoreSqliteDataStore(...)`. The YesSql and Entity Framework Core packages are both optional: consumers can register either flavor or replace them with their own persistence layer entirely. After configuring the EF Core data store, call `AddEntityCoreStores()` to register the built-in CrestApps stores and catalog services.

Reusable AI-related YesSql index models, `IndexProvider` types, and schema helpers now live in `CrestApps.Core.Data.YesSql` as well, so hosts can register the shared AI storage surface without copying provider implementations out of the sample app. Within that project, feature assets are grouped directly under `Indexes/{Feature}`, their namespaces follow the same `CrestApps.Core.Data.YesSql.Indexes.{Feature}` shape, each shared schema-helper file is scoped to a single index type, and each shared index file now keeps the index model beside its matching `IndexProvider` for easier maintenance.

The MVC sample also normalizes legacy stored `Document.Type` values during YesSql startup so local `App_Data` databases remain readable after framework, infrastructure, or MVC assembly renames. It registers its shared YesSql `IndexProvider` types during that startup initialization step as well, which keeps the `IStore` registration itself simple and avoids recursive DI when the host resolves the store for the first time.

## Problem & Solution

The framework defines many model types (profiles, deployments, connections, sessions, documents) that need persistent storage. Rather than coupling to a specific ORM, it uses the **catalog pattern**:

- **`ICatalog<T>`** — Basic CRUD operations
- **`INamedCatalog<T>`** — Adds name-based lookup
- **`ISourceCatalog<T>`** — Adds source-based filtering
- **`INamedSourceCatalog<T>`** — Combines both

The repository now ships two first-party persistence flavors:

| Package | Backing technology | Typical fit |
|---------|--------------------|-------------|
| `CrestApps.Core.Data.YesSql` | YesSql document store | Hosts that want YesSql collections, indexes, and a request-scoped unit of work |
| `CrestApps.Core.Data.EntityCore` | Entity Framework Core | Hosts that already standardize on EF Core and want SQLite or another EF-supported relational provider |

You can also implement the same interfaces with another ORM, a remote service, or any custom storage approach.

## Catalog Interfaces

### `ICatalog<T>`

Basic CRUD:

```csharp
public interface ICatalog<T> : IReadCatalog<T>
{
    ValueTask CreateAsync(T entry);
    ValueTask UpdateAsync(T entry);
    ValueTask<bool> DeleteAsync(T entry);
}
```

The YesSql implementation **stages** writes only. Hosts using `CrestApps.Core.Data.YesSql` should flush the YesSql session at the end of the HTTP request, SignalR hub method, or background operation that performed the write. The Entity Framework Core implementation commits after each write operation via an individual `SaveChangesAsync()` call inside each store method. Every create, update, and delete is durable immediately; no request-level middleware is required or expected for the built-in stores.

### `INamedCatalog<T>`

Adds name-based lookup for models implementing `INameAwareModel`:

```csharp
public interface INamedCatalog<T> : ICatalog<T> where T : INameAwareModel
{
    ValueTask<T> FindByNameAsync(string name);
}
```

### `ISourceCatalog<T>`

Adds source-based filtering for models implementing `ISourceAwareModel`:

```csharp
public interface ISourceCatalog<T> : ICatalog<T> where T : ISourceAwareModel
{
    ValueTask<IReadOnlyCollection<T>> GetAsync(string source);
}
```

### `INamedSourceCatalog<T>`

Combines both capabilities:

```csharp
public interface INamedSourceCatalog<T> : INamedCatalog<T>, ISourceCatalog<T>
    where T : INameAwareModel, ISourceAwareModel
{
    ValueTask<T> GetAsync(string name, string source);
}
```

## DI Extension Methods

### YesSql catalog extensions

| Method | Registers | Requires |
|--------|-----------|----------|
| `AddYesSqlDocumentCatalog<TModel, TIndex>()` | `ICatalog<T>` | `CatalogItem` + `CatalogItemIndex` |
| `AddYesSqlNamedDocumentCatalog<TModel, TIndex>()` | `ICatalog<T>` + `INamedCatalog<T>` | + `INameAwareModel` + `INameAwareIndex` |
| `AddYesSqlSourceDocumentCatalog<TModel, TIndex>()` | `ICatalog<T>` + `ISourceCatalog<T>` | + `ISourceAwareModel` + `ISourceAwareIndex` |
| `AddYesSqlNamedSourceDocumentCatalog<TModel, TIndex>()` | All four interfaces | Both `INameAware*` + `ISourceAware*` |

### YesSql binding source extensions

These register a YesSql-backed catalog as a **binding source** for the multi-source store pattern (see [Multi-Source Binding Pattern](#multi-source-binding-pattern) below):

| Method | Binding source registered | Requires |
|--------|--------------------------|----------|
| `AddYesSqlNamedSourceBindingSource<TModel, TIndex>()` | `INamedSourceCatalogSource<TModel>` | `CatalogItem` + both `INameAware*` + `ISourceAware*` |
| `AddYesSqlNamedBindingSource<TModel, TIndex>()` | `INamedCatalogSource<TModel>` | `CatalogItem` + `INameAwareModel` |

### Entity Framework Core catalog extensions

The Entity Framework Core package exposes the same service-registration shape without YesSql indexes:

| Method | Registers | Requires |
|--------|-----------|----------|
| `AddDocumentCatalog<TModel>()` | `ICatalog<T>` | `CatalogItem` |
| `AddNamedDocumentCatalog<TModel>()` | `ICatalog<T>` + `INamedCatalog<T>` | `CatalogItem` + `INameAwareModel` |
| `AddSourceDocumentCatalog<TModel>()` | `ICatalog<T>` + `ISourceCatalog<T>` | `CatalogItem` + `ISourceAwareModel` |
| `AddNamedSourceDocumentCatalog<TModel>()` | All four interfaces | `CatalogItem` + both awareness interfaces |

### Entity Framework Core binding source extensions

These register an EntityCore-backed catalog as a **binding source** for the multi-source store pattern:

| Method | Binding source registered | Requires |
|--------|--------------------------|----------|
| `AddEntityCoreNamedSourceBindingSource<TModel>()` | `INamedSourceCatalogSource<TModel>` | `SourceCatalogEntry` + `INameAwareModel` |
| `AddEntityCoreNamedBindingSource<TModel>()` | `INamedCatalogSource<TModel>` | `CatalogItem` + `INameAwareModel` |

### Bulk store registration

`AddEntityCoreStores()` registers the built-in CrestApps store interfaces (`IAIChatSessionManager`, prompt stores, document stores, memory stores, search index profile store, and related catalog registrations) against the Entity Framework Core package. It also registers the multi-source binding sources for `AIProviderConnection` and `AIDeployment`.


## Catalog Entry Handlers

React to lifecycle events on catalog entries:

```csharp
public sealed class ProfileCreatedHandler : CatalogEntryHandlerBase<AIProfile>
{
    public override Task CreatedAsync(CreatedContext<AIProfile> context)
    {
        // React to profile creation (e.g., initialize defaults, send notification)
        return Task.CompletedTask;
    }
}

// Register
builder.Services.AddScoped<ICatalogEntryHandler<AIProfile>, ProfileCreatedHandler>();
```

### Lifecycle Events

| Phase | Context Types |
|-------|--------------|
| Initialize | `InitializingContext<T>`, `InitializedContext<T>` |
| Validate | `ValidatingContext<T>`, `ValidatedContext<T>` |
| Create | `CreatingContext<T>`, `CreatedContext<T>` |
| Update | `UpdatingContext<T>`, `UpdatedContext<T>` |
| Delete | `DeletingContext<T>`, `DeletedContext<T>` |
| Load | `LoadingContext<T>`, `LoadedContext<T>` |

## YesSql Index Pattern

Each model needs a corresponding YesSql index:

```csharp
public sealed class AIProfileIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    public string Name { get; set; }
    public string Source { get; set; }
}

public sealed class AIProfileIndexProvider : IndexProvider<AIProfile>
{
    public override void Describe(DescribeContext<AIProfile> context)
    {
        context.For<AIProfileIndex>()
            .Map(profile => new AIProfileIndex
            {
                ItemId = profile.Id,
                Name = profile.Name,
                Source = profile.Source,
            });
    }
}
```

In `CrestApps.Core.Data.YesSql`, the shared convention is to keep each index type and its matching provider in the same file under `Indexes/{Feature}`. That keeps the document shape, mapping logic, and folder-based namespace together while leaving `*SchemaBuilderExtensions` in separate migration-oriented files.

## Model Types in the Framework

| Model | Catalog Type | Used By |
|-------|-------------|---------|
| `AIProfile` | Named + Source | AI profiles and configuration |
| `AIProviderConnection` | Named + Source | Provider credentials |
| `AIDeployment` | Named + Source | Model deployment mappings |
| `AIProfileTemplate` | Named + Source | Profile templates |
| `AIChatSession` | Basic | Chat sessions |
| `ChatInteraction` | Basic | Chat interactions |
| `McpConnection` | Source | MCP server connections |
| `McpPrompt` | Named | MCP prompts |
| `McpResource` | Source | MCP resources |
| `A2AConnection` | Basic | A2A connections |

## Feature Store Requirements

Each feature requires specific stores to be registered. The table below lists what each feature needs and the corresponding registration calls for YesSql and Entity Framework Core.

:::tip
`AddCoreAIServices()` registers the multi-source stores for `AIDeployment` and `AIProviderConnection` with appsettings-backed binding sources automatically. You only need to register the persistence-layer binding sources to enable database-backed storage.
:::

### Core AI (always required)

Registered automatically by `AddCoreAIServices()`:

| Model | Store interface | Registration |
|-------|----------------|--------------|
| `AIDeployment` | `IAIDeploymentStore` | Auto-registered (multi-source, config source at Order 100) |
| `AIProviderConnection` | `IAIProviderConnectionStore` | Auto-registered (multi-source, config source at Order 100) |

Add a DB binding source if you want database-backed deployments and connections:

```csharp
// YesSql
services.AddYesSqlNamedSourceBindingSource<AIDeployment, AIDeploymentIndex>();
services.AddYesSqlNamedSourceBindingSource<AIProviderConnection, AIProviderConnectionIndex>();

// Entity Framework Core (included in AddEntityCoreStores())
services.AddEntityCoreNamedSourceBindingSource<AIDeployment>();
services.AddEntityCoreNamedSourceBindingSource<AIProviderConnection>();
```

### AI Profiles

| Model | Catalog registration | YesSql | EntityCore |
|-------|---------------------|--------|------------|
| `AIProfile` | `INamedSourceCatalog<AIProfile>` | `AddYesSqlNamedSourceDocumentCatalog<AIProfile, AIProfileIndex>()` | `AddNamedSourceDocumentCatalog<AIProfile, NamedSourceDocumentCatalog<AIProfile>>()` |
| `AIProfileTemplate` | `INamedSourceCatalog<AIProfileTemplate>` | `AddYesSqlNamedSourceDocumentCatalog<AIProfileTemplate, AIProfileTemplateIndex>()` | `AddNamedSourceDocumentCatalog<AIProfileTemplate, NamedSourceDocumentCatalog<AIProfileTemplate>>()` |

### Chat

| Model | Store interface | Registration |
|-------|----------------|--------------|
| `AIChatSession` | `IAIChatSessionManager` | `AddScoped<IAIChatSessionManager, YesSqlAIChatSessionManager>()` (YesSql) or `EntityCoreAIChatSessionManager` (EF Core) |
| `AIChatSessionPrompt` | `IAIChatSessionPromptStore` | `AddScoped<IAIChatSessionPromptStore, YesSqlAIChatSessionPromptStore>()` (YesSql) or `EntityCoreAIChatSessionPromptStore` (EF Core) |

### Chat Interactions

| Model | Catalog registration | YesSql | EntityCore |
|-------|---------------------|--------|------------|
| `ChatInteraction` | `ICatalog<ChatInteraction>` | `AddYesSqlDocumentCatalog<ChatInteraction, ChatInteractionIndex>()` | `AddDocumentCatalog<ChatInteraction, DocumentCatalog<ChatInteraction>>()` |
| `ChatInteractionPrompt` | `IChatInteractionPromptStore` | `AddScoped<IChatInteractionPromptStore, YesSqlChatInteractionPromptStore>()` | `EntityCoreChatInteractionPromptStore` |

### Documents and Data Sources

| Model | Store interface | Registration |
|-------|----------------|--------------|
| `AIDocument` | `IAIDocumentStore` | `AddScoped<IAIDocumentStore, YesSqlAIDocumentStore>()` or `EntityCoreAIDocumentStore` |
| `AIDocumentChunk` | `IAIDocumentChunkStore` | `AddScoped<IAIDocumentChunkStore, YesSqlAIDocumentChunkStore>()` or `EntityCoreAIDocumentChunkStore` |
| `AIDataSource` | `IAIDataSourceStore` | `AddScoped<IAIDataSourceStore, YesSqlAIDataSourceStore>()` or `EntityCoreAIDataSourceStore` |
| `SearchIndexProfile` | `ISearchIndexProfileStore` | `AddScoped<ISearchIndexProfileStore, YesSqlSearchIndexProfileStore>()` or `EntityCoreSearchIndexProfileStore` |

### Memory

| Model | Store interface | Registration |
|-------|----------------|--------------|
| `AIMemoryEntry` | `IAIMemoryStore` | `AddScoped<IAIMemoryStore, YesSqlAIMemoryStore>()` or `EntityCoreAIMemoryStore` |

### MCP (Model Context Protocol)

| Model | Catalog registration | YesSql | EntityCore |
|-------|---------------------|--------|------------|
| `McpConnection` | `ISourceCatalog<McpConnection>` | `AddYesSqlSourceDocumentCatalog<McpConnection, McpConnectionIndex>()` | `AddSourceDocumentCatalog<McpConnection, SourceDocumentCatalog<McpConnection>>()` |
| `McpPrompt` | `INamedCatalog<McpPrompt>` | `AddYesSqlNamedDocumentCatalog<McpPrompt, McpPromptIndex>()` | `AddNamedDocumentCatalog<McpPrompt, NamedDocumentCatalog<McpPrompt>>()` |
| `McpResource` | `ISourceCatalog<McpResource>` | `AddYesSqlSourceDocumentCatalog<McpResource, McpResourceIndex>()` | `AddSourceDocumentCatalog<McpResource, SourceDocumentCatalog<McpResource>>()` |

### A2A (Agent-to-Agent)

| Model | Catalog registration | YesSql | EntityCore |
|-------|---------------------|--------|------------|
| `A2AConnection` | `ICatalog<A2AConnection>` | `AddYesSqlDocumentCatalog<A2AConnection, A2AConnectionIndex>()` | `AddDocumentCatalog<A2AConnection, DocumentCatalog<A2AConnection>>()` |

:::note
`AddEntityCoreStores()` registers all of the above EntityCore stores in a single call. YesSql hosts must register each catalog individually because they also need to register index providers and create index tables during startup.
:::

## First-party Entity Framework Core package

The `CrestApps.Core.Data.EntityCore` package gives you a ready-made alternative when you want the CrestApps store surface without YesSql.

### SQLite local development

```csharp
builder.Services.AddCoreEntityCoreSqliteDataStore(
    $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "App_Data", "crestapps.db")}");

builder.Services.AddEntityCoreStores();
```

Create the schema during startup:

```csharp
var app = builder.Build();

await app.Services.InitializeEntityCoreSchemaAsync();
```

The package stores framework records in EF Core-managed tables and keeps the same catalog/store abstractions available to the rest of the application.

### Chat session atomicity

`IAIChatSessionManager.NewAsync` accepts an optional initial prompt. When called, the prompt is staged in the EF Core change tracker alongside the new session record. `SaveAsync` then commits both the session and the initial prompt atomically in a single `SaveChangesAsync` call. If `SaveAsync` is never called, the scoped `DbContext` disposes without committing, so no orphaned prompt record is written.

## Automatic store commit (`IStoreCommitter`)

The YesSql store package stages writes in memory and flushes them to the database as a single transaction when `ISession.SaveChangesAsync()` is called. Instead of requiring you to call that flush manually in every controller action and hub method, the framework provides `IStoreCommitter` — a thin interface that abstracts the flush signal.

```csharp
public interface IStoreCommitter
{
    ValueTask CommitAsync(CancellationToken cancellationToken = default);
}
```

`AddCoreYesSqlDataStore()` registers `YesSqlStoreCommitter` as the scoped `IStoreCommitter`. The Entity Framework Core package does **not** register a committer because its stores commit on every individual write operation.

### Automatic commit for MVC controllers

Add `AddCrestAppsStoreCommitterFilter()` to your MVC builder. The filter wraps every controller action and calls `IStoreCommitter.CommitAsync()` after a successful response:

```csharp
builder.Services
    .AddControllersWithViews()
    .AddCrestAppsStoreCommitterFilter();
```

### Automatic commit for SignalR hub methods

Pass `AddCrestAppsStoreCommitterFilter()` to the SignalR builder. The hub filter calls `IStoreCommitter.CommitAsync()` after each hub method returns:

```csharp
builder.Services.AddSignalR().AddCrestAppsStoreCommitterFilter();
```

:::note
Hub methods that perform fire-and-forget async work (such as streaming responses through a `ChannelReader`) must call `IStoreCommitter.CommitAsync()` explicitly inside the async lambda. The hub filter commits after the hub method itself returns, which is before the fire-and-forget work completes.
:::

### Automatic commit for Minimal API endpoints

Apply `StoreCommitterEndpointFilter` to a route group or individual endpoints:

```csharp
app.MapGroup("/api")
    .AddEndpointFilter<StoreCommitterEndpointFilter>();
```

### Providing a custom committer

If you build a custom store that stages writes, implement `IStoreCommitter` and register it as scoped:

```csharp
services.AddScoped<IStoreCommitter, MyCustomStoreCommitter>();
```

The filter infrastructure calls your committer automatically — no other wiring is required.

## Multi-Source Binding Pattern

When a model needs entries that come from more than one place — for example, AI deployments defined in `appsettings.json` merged with deployments stored in a database — the framework uses **binding sources**. Each source supplies entries independently, and a multi-source store aggregates them at runtime, deduplicating by name (the lowest-order source wins).

### How it works

```text
┌────────────────────────────────────────────────────────┐
│  DefaultAIDeploymentStore (MultiSourceNamedSourceCatalog)  │
│                                                        │
│  ┌─────────────────────┐  ┌─────────────────────────┐  │
│  │ DB binding source   │  │ Config binding source    │  │
│  │ Order = 0 (wins)    │  │ Order = 100 (fallback)  │  │
│  │ YesSql / EntityCore │  │ appsettings.json        │  │
│  └─────────────────────┘  └─────────────────────────┘  │
└────────────────────────────────────────────────────────┘
```

1. Each **binding source** implements `INamedSourceCatalogSource<T>` (or `INamedCatalogSource<T>` for models without a source property).
2. Sources declare an `Order` property — lower values win when two sources provide entries with the same name.
3. The **multi-source store** iterates all sources in order and builds a deduplicated list.
4. Write operations (create, update, delete) are delegated to the first **writable** source.

### Binding source interfaces

```csharp
// Read-only source for named models
public interface INamedCatalogSource<T> where T : INameAwareModel
{
    int Order { get; }
    ValueTask<IReadOnlyCollection<T>> GetEntriesAsync(IReadOnlyCollection<T> knownEntries);
}

// Read-only source for named + source-aware models
public interface INamedSourceCatalogSource<T> : INamedCatalogSource<T>
    where T : INameAwareModel, ISourceAwareModel { }

// Writable source for named models
public interface IWritableNamedCatalogSource<T> : INamedCatalogSource<T>
    where T : INameAwareModel
{
    ValueTask<bool> DeleteAsync(T entry);
    ValueTask CreateAsync(T entry);
    ValueTask UpdateAsync(T entry);
}

// Writable source for named + source-aware models
public interface IWritableNamedSourceCatalogSource<T>
    : INamedSourceCatalogSource<T>, IWritableNamedCatalogSource<T>
    where T : INameAwareModel, ISourceAwareModel { }
```

### Base classes

The framework provides two abstract base classes that handle merging, deduplication, filtering, pagination, and write delegation:

| Base class | For models that implement | Implements |
|------------|--------------------------|------------|
| `MultiSourceNamedCatalog<T>` | `INameAwareModel` | `INamedCatalog<T>` |
| `MultiSourceNamedSourceCatalog<T>` | `INameAwareModel` + `ISourceAwareModel` | `INamedSourceCatalog<T>` |

Both accept `IEnumerable<INamedCatalogSource<T>>` (or the source-aware variant) via constructor injection, order the sources by `Order`, and merge entries by name.

### Built-in stores

`AddCoreAIServices()` registers two multi-source stores and their default configuration-backed binding sources automatically:

| Store | Implements | Binding sources |
|-------|-----------|-----------------|
| `DefaultAIDeploymentStore` | `IAIDeploymentStore` → `INamedSourceCatalog<AIDeployment>` | `ConfigurationAIDeploymentSource` (Order 100) |
| `DefaultAIProviderConnectionStore` | `IAIProviderConnectionStore` → `INamedSourceCatalog<AIProviderConnection>` | `ConfigurationAIProviderConnectionSource` (Order 100) |

The configuration sources read from `appsettings.json` using the sections configured in `AIDeploymentCatalogOptions` and `AIProviderConnectionCatalogOptions`:

| Options class | Default sections |
|--------------|------------------|
| `AIDeploymentCatalogOptions` | `CrestApps:AI:Deployments` |
| `AIProviderConnectionCatalogOptions` | `CrestApps:AI:Connections` (connection sections) and `CrestApps:AI:Providers` (provider sections) |

When a persistence package (YesSql or EntityCore) is added, it registers an additional **writable** DB binding source at Order 0, so database entries take priority over `appsettings.json` entries and all write operations go to the database.

### Registering DB binding sources

#### YesSql

```csharp
// Register a YesSql-backed writable binding source for AI deployments
services.AddYesSqlNamedSourceBindingSource<AIDeployment, AIDeploymentIndex>();

// Register a YesSql-backed writable binding source for AI connections
services.AddYesSqlNamedSourceBindingSource<AIProviderConnection, AIProviderConnectionIndex>();
```

#### Entity Framework Core

```csharp
// Register an EntityCore-backed writable binding source for AI deployments
services.AddEntityCoreNamedSourceBindingSource<AIDeployment>();

// Register an EntityCore-backed writable binding source for AI connections
services.AddEntityCoreNamedSourceBindingSource<AIProviderConnection>();
```

:::info
`AddEntityCoreStores()` already calls both of the above registrations. You only need to call them explicitly when composing your own store registration.
:::

### How binding source adapters work

The framework provides two generic adapter classes that wrap an existing catalog as a writable binding source:

| Adapter | Wraps | For models with |
|---------|-------|-----------------|
| `WritableCatalogBindingSource<T>` | `INamedSourceCatalog<T>` | Name + Source |
| `WritableNamedCatalogBindingSource<T>` | `INamedCatalog<T>` | Name only |

Both set `Order = 0` (highest priority) and delegate all read and write operations to the wrapped catalog. The generic YesSql and EntityCore extension methods use these adapters internally.

### Creating a custom binding source

To add entries from any source (remote API, file system, embedded resources, etc.), implement `INamedSourceCatalogSource<T>` (or `INamedCatalogSource<T>` for named-only models):

```csharp
public sealed class RemoteApiDeploymentSource : INamedSourceCatalogSource<AIDeployment>
{
    private readonly IRemoteDeploymentClient _client;

    public RemoteApiDeploymentSource(IRemoteDeploymentClient client)
    {
        _client = client;
    }

    // Order 50 — higher priority than config (100), lower than DB (0)
    public int Order => 50;

    public async ValueTask<IReadOnlyCollection<AIDeployment>> GetEntriesAsync(
        IReadOnlyCollection<AIDeployment> knownEntries)
    {
        // knownEntries contains entries from higher-priority sources (DB, etc.)
        // Use it to skip entries whose names already exist.
        var existingNames = knownEntries
            .Select(e => e.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var remoteDeployments = await _client.GetDeploymentsAsync();

        return remoteDeployments
            .Where(d => !existingNames.Contains(d.Name))
            .ToArray();
    }
}
```

Register it with `TryAddEnumerable` so that it is additive:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<INamedSourceCatalogSource<AIDeployment>, RemoteApiDeploymentSource>());
```

The multi-source store discovers all registered `INamedSourceCatalogSource<AIDeployment>` instances and merges them automatically. No changes are needed to existing store or controller code.

### Priority and deduplication rules

| Priority | Source | Typical registration |
|----------|--------|---------------------|
| 0 (highest) | Database (YesSql / EntityCore) | `AddYesSqlNamedSourceBindingSource` or `AddEntityCoreNamedSourceBindingSource` |
| 1–99 | Custom sources | Registered by application code |
| 100 (lowest) | Configuration (`appsettings.json`) | Registered by `AddCoreAIServices()` |

When two sources provide an entry with the same `Name`, the entry from the lower-order source wins and the duplicate is skipped.

### Building a custom multi-source store

If you need multi-source behavior for your own model type, extend the appropriate base class:

```csharp
public sealed class DefaultWidgetStore : MultiSourceNamedSourceCatalog<Widget>, IWidgetStore
{
    public DefaultWidgetStore(IEnumerable<INamedSourceCatalogSource<Widget>> sources)
        : base(sources)
    {
    }

    protected override string GetItemId(Widget entry) => entry.ItemId;
}
```

Then register the store and its configuration source:

```csharp
// Store registration
services.TryAddScoped<IWidgetStore, DefaultWidgetStore>();
services.TryAddScoped<INamedSourceCatalog<Widget>>(sp => sp.GetRequiredService<IWidgetStore>());

// Config-backed source (Order = 100)
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<INamedSourceCatalogSource<Widget>, ConfigurationWidgetSource>());

// DB-backed source (Order = 0) — YesSql example
services.AddYesSqlNamedSourceBindingSource<Widget, WidgetIndex>();
```

## Pagination

Catalogs support paginated queries through the `PageAsync` method. The YesSql-backed `DocumentCatalog` uses `.Skip()` and `.Take()` on the underlying query:

```csharp
// In a controller or service
public async Task<IActionResult> List(int page = 1, int pageSize = 20)
{
    var catalog = HttpContext.RequestServices.GetRequiredService<ICatalog<AIProfile>>();

    // PageAsync returns a PageResult<T> with Count and Entries
    var result = await catalog.PageAsync(page, pageSize);

    // result.Count  — total number of matching entries
    // result.Entries — the current page of items
    return View(new ListViewModel
    {
        Items = result.Entries,
        TotalCount = result.Count,
        Page = page,
        PageSize = pageSize,
    });
}
```

Under the hood, the YesSql implementation computes the skip value and applies it to the query:

```csharp
var skip = (page - 1) * pageSize;
var entries = await query.Skip(skip).Take(pageSize).ListAsync();
var count = await query.CountAsync();

return new PageResult<T>
{
    Count = count,
    Entries = entries.ToArray(),
};
```

:::info
YesSql translates `.Skip()` and `.Take()` into database-native `OFFSET`/`LIMIT` (SQLite, PostgreSQL) or `OFFSET`/`FETCH` (SQL Server) clauses. No in-memory paging is performed.
:::

## Bulk Operations

When inserting or updating many entries at once, use a **batch loop** pattern with periodic `SaveChangesAsync()` calls to avoid excessive memory use:

```csharp
private const int _batchSize = 50;

public async Task ImportProfilesAsync(IStore store, IList<AIProfile> profiles)
{
    for (var batchStart = 0; batchStart < profiles.Count; batchStart += _batchSize)
    {
        var batch = profiles.Skip(batchStart).Take(_batchSize).ToList();

        // Create a fresh session per batch to control memory
        using var session = store.CreateSession();

        foreach (var profile in batch)
        {
            await session.SaveAsync(profile, collection: AIConstants.AICollectionName);
        }

        await session.SaveChangesAsync();
    }
}
```

:::warning
YesSql does not support SQL-level `INSERT ... VALUES (...), (...)` bulk inserts. Each `SaveAsync` call tracks the entity in the session's identity map. Flushing per batch (via `SaveChangesAsync()` and disposing the session) prevents the identity map from growing unbounded.
:::

For scenarios where you process existing records in batches (e.g., recipe imports), combine pagination with batch updates:

```csharp
private const int _batchSize = 250;

public async Task UpdateAllUsersAsync(ISession session)
{
    var currentBatch = 0;

    while (true)
    {
        var users = await session.Query<User, UserIndex>(u => u.IsEnabled)
            .OrderBy(x => x.DocumentId)
            .Skip(currentBatch)
            .Take(_batchSize)
            .ListAsync();

        if (!users.Any())
        {
            break;
        }

        foreach (var user in users)
        {
            // Apply updates
            user.Properties["MigratedAt"] = DateTime.UtcNow.ToString("O");
            await session.SaveAsync(user);
        }

        await session.SaveChangesAsync();
        currentBatch += _batchSize;
    }
}
```

## Migration Strategy

When your model's schema changes (e.g., a new column is added to an index), you need a YesSql **index migration** to update the database. Migrations use `SchemaBuilder` to alter index tables.

### Creating an Initial Index

```csharp
public sealed class MyModelIndexMigrations : DataMigration
{
    public async Task<int> CreateAsync()
    {
        await SchemaBuilder.CreateMapIndexTableAsync<MyModelIndex>(table => table
            .Column<string>("ItemId", column => column.WithLength(26))
            .Column<string>("Name", column => column.WithLength(255))
            .Column<string>("Source", column => column.WithLength(255)),
            collection: MyConstants.CollectionName
        );

        await SchemaBuilder.AlterIndexTableAsync<MyModelIndex>(table => table
            .CreateIndex("IDX_MyModelIndex_DocumentId",
                "DocumentId",
                "ItemId",
                "Name",
                "Source"),
            collection: MyConstants.CollectionName
        );

        return 1;
    }
}
```

### Adding a Column in a Later Version

Use `UpdateFromNAsync` methods to add columns or indexes incrementally:

```csharp
public async Task<int> UpdateFrom1Async()
{
    // Add a new column to an existing index
    await SchemaBuilder.AlterIndexTableAsync<MyModelIndex>(table => table
        .AddColumn<string>("DeploymentName", column => column.Nullable().WithLength(255)),
        collection: MyConstants.CollectionName
    );

    return 2;
}

public async Task<int> UpdateFrom2Async()
{
    // Add a compound index for new query patterns
    await SchemaBuilder.AlterIndexTableAsync<MyModelIndex>(table => table
        .CreateIndex("IDX_MyModelIndex_Deployment",
            "DocumentId",
            "DeploymentName",
            "Source"),
        collection: MyConstants.CollectionName
    );

    return 3;
}
```

:::tip
Always mark new columns as `.Nullable()` so existing rows are not affected. Backfill data in a separate migration step if needed.
:::

### Data Backfill During Migration

When a schema change requires backfilling existing data, combine `SchemaBuilder` with a batch update:

```csharp
public async Task<int> UpdateFrom3Async()
{
    // Step 1: Add the new column
    await SchemaBuilder.AlterIndexTableAsync<AIProfileIndex>(table => table
        .AddColumn<bool>("IsActive", column => column.Nullable().WithDefault(true)),
        collection: AIConstants.AICollectionName
    );

    // Step 2: Backfill existing records
    var store = HttpContext.RequestServices.GetRequiredService<IStore>();

    using var session = store.CreateSession();

    var profiles = await session.Query<AIProfile, AIProfileIndex>(
        collection: AIConstants.AICollectionName).ListAsync();

    foreach (var profile in profiles)
    {
        profile.IsActive = true;
        await session.SaveAsync(profile, collection: AIConstants.AICollectionName);
    }

    await session.SaveChangesAsync();

    return 4;
}
```

## Implementing your own backend

If neither first-party package matches your persistence model, implement the same abstractions yourself. A custom backend only needs to satisfy the catalog/store interfaces already consumed by the framework.

```csharp
public sealed class CustomCatalog<T> : ICatalog<T> where T : CatalogItem
{
    private readonly MyStoreClient _client;

    public CustomCatalog(MyStoreClient client) => _client = client;

    // IReadCatalog<T>
    public async ValueTask<T> FindAsync(string id)
        => await _client.FindAsync<T>(id);

    public async ValueTask<IReadOnlyCollection<T>> GetAllAsync()
        => await _client.ListAsync<T>();

    public async ValueTask<PageResult<T>> PageAsync(int page, int pageSize)
    {
        return await _client.PageAsync<T>(page, pageSize);
    }

    // ICatalog<T>
    public async ValueTask CreateAsync(T entry)
        => await _client.CreateAsync(entry);

    public ValueTask UpdateAsync(T entry)
    {
        _client.Update(entry);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteAsync(T entry)
    {
        return _client.DeleteAsync(entry);
    }
}
```

For named and source-aware models, extend with the additional interfaces:

```csharp
public sealed class CustomNamedSourceCatalog<T> : CustomCatalog<T>, INamedSourceCatalog<T>
    where T : CatalogItem, INameAwareModel, ISourceAwareModel
{
    private readonly MyStoreClient _client;

    public CustomNamedSourceCatalog(MyStoreClient client) : base(client) => _client = client;

    public async ValueTask<T> FindByNameAsync(string name)
        => await _client.FindByNameAsync<T>(name);

    public async ValueTask<IReadOnlyCollection<T>> GetAsync(string source)
        => await _client.ListBySourceAsync<T>(source);

    public async ValueTask<T> GetAsync(string name, string source)
        => await _client.FindByNameAndSourceAsync<T>(name, source);
}
```

Register in DI:

```csharp
builder.Services.AddScoped<ICatalog<AIProfile>, CustomNamedSourceCatalog<AIProfile>>();
builder.Services.AddScoped<INamedCatalog<AIProfile>, CustomNamedSourceCatalog<AIProfile>>();
builder.Services.AddScoped<ISourceCatalog<AIProfile>, CustomNamedSourceCatalog<AIProfile>>();
builder.Services.AddScoped<INamedSourceCatalog<AIProfile>, CustomNamedSourceCatalog<AIProfile>>();
```


