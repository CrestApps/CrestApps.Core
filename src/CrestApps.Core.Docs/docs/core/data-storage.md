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

builder.Services.AddNamedSourceDocumentCatalog<AIProfile, AIProfileIndex>();

// Entity Framework Core + SQLite
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddEntityCoreSqliteDataStore("Data Source=app.db"));
builder.Services.AddCoreEntityCoreStores();
```

`AddYesSqlDataStore(...)` on the root CrestApps builder maps to `AddCoreYesSqlDataStore(...)`, while `AddEntityCoreSqliteDataStore(...)` maps to `AddCoreEntityCoreSqliteDataStore(...)`. The YesSql and Entity Framework Core packages are both optional: consumers can register either flavor or replace them with their own persistence layer entirely.

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

| Method | Registers | Requires |
|--------|-----------|----------|
| `AddDocumentCatalog<TModel, TIndex>()` | `ICatalog<T>` | `CatalogItem` + `CatalogItemIndex` |
| `AddNamedDocumentCatalog<TModel, TIndex>()` | `ICatalog<T>` + `INamedCatalog<T>` | + `INameAwareModel` + `INameAwareIndex` |
| `AddSourceDocumentCatalog<TModel, TIndex>()` | `ICatalog<T>` + `ISourceCatalog<T>` | + `ISourceAwareModel` + `ISourceAwareIndex` |
| `AddNamedSourceDocumentCatalog<TModel, TIndex>()` | All four interfaces | Both `INameAware*` + `ISourceAware*` |

The Entity Framework Core package exposes the same service-registration shape without YesSql indexes:

| Method | Registers | Requires |
|--------|-----------|----------|
| `AddDocumentCatalog<TModel>()` | `ICatalog<T>` | `CatalogItem` |
| `AddNamedDocumentCatalog<TModel>()` | `ICatalog<T>` + `INamedCatalog<T>` | `CatalogItem` + `INameAwareModel` |
| `AddSourceDocumentCatalog<TModel>()` | `ICatalog<T>` + `ISourceCatalog<T>` | `CatalogItem` + `ISourceAwareModel` |
| `AddNamedSourceDocumentCatalog<TModel>()` | All four interfaces | `CatalogItem` + both awareness interfaces |

`AddCoreEntityCoreStores()` registers the built-in CrestApps store interfaces (`IAIChatSessionManager`, prompt stores, document stores, memory stores, search index profile store, and related catalog registrations) against the Entity Framework Core package.


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

## First-party Entity Framework Core package

The `CrestApps.Core.Data.EntityCore` package gives you a ready-made alternative when you want the CrestApps store surface without YesSql.

### SQLite local development

```csharp
builder.Services.AddCoreEntityCoreSqliteDataStore(
    $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "App_Data", "crestapps.db")}");

builder.Services.AddCoreEntityCoreStores();
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

## Composite Catalogs

When models need to be loaded from multiple sources (e.g., code-defined defaults merged with database entries), use the **CatalogManager** pattern. The `CatalogManager<T>` delegates reads across all registered `ICatalog<T>` instances and merges the results:

```csharp
public sealed class CatalogManager<T>(
    ICatalog<T> primaryCatalog,
    IEnumerable<IReadCatalog<T>> additionalSources) where T : CatalogItem
{
    public async ValueTask<IReadOnlyCollection<T>> GetAllAsync()
    {
        var results = new List<T>();

        // Load from the primary (writable) catalog
        results.AddRange(await primaryCatalog.GetAllAsync());

        // Merge from read-only additional sources
        foreach (var source in additionalSources)
        {
            var entries = await source.GetAllAsync();
            foreach (var entry in entries)
            {
                if (!results.Any(r => r.Id == entry.Id))
                {
                    results.Add(entry);
                }
            }
        }

        return results;
    }
}
```

Register additional read-only sources alongside the primary catalog:

```csharp
// Primary writable catalog (YesSql-backed)
builder.Services.AddNamedSourceDocumentCatalog<AIProfile, AIProfileIndex>();

// Additional read-only source (e.g., code-defined defaults)
builder.Services.AddScoped<IReadCatalog<AIProfile>, DefaultProfilesCatalog>();
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


