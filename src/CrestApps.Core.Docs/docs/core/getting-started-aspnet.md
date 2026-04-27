---
title: ASP.NET Core Integration
sidebar_position: 2
description: Register CrestApps.Core services in MVC, Razor Pages, Blazor, Minimal APIs, and .NET MAUI hybrid hosts.
---

# Getting Started with ASP.NET Core

This guide shows how to add `CrestApps.Core` services to any ASP.NET Core host model. The same framework packages and registration chain work across MVC, Razor Pages, Blazor, Minimal APIs, and .NET MAUI hybrid apps.

## 1. Choose packages

Start with the smallest set that matches your scenario.

```xml
<ItemGroup>
  <PackageReference Include="CrestApps.Core" />
  <PackageReference Include="CrestApps.Core.AI" />

  <PackageReference Include="CrestApps.Core.AI.Chat" />
  <!-- Add when you need uploaded-document ingestion, document RAG, or document endpoints -->
  <PackageReference Include="CrestApps.Core.AI.Documents" />
  <PackageReference Include="CrestApps.Core.AI.Documents.OpenXml" />
  <PackageReference Include="CrestApps.Core.AI.Documents.Pdf" />

  <!-- Pick at least one provider -->
  <PackageReference Include="CrestApps.Core.AI.OpenAI" />
  <!-- or CrestApps.Core.AI.OpenAI.Azure -->
  <!-- or CrestApps.Core.AI.Ollama -->
  <!-- or CrestApps.Core.AI.AzureAIInference -->

  <!-- Optional but common -->
  <PackageReference Include="CrestApps.Core.AI.Markdown" />
  <PackageReference Include="CrestApps.Core.Templates" />
  <PackageReference Include="CrestApps.Core.SignalR" />
  <PackageReference Include="CrestApps.Core.Data.EntityCore" />
  <!-- or CrestApps.Core.Data.YesSql -->
</ItemGroup>
```

## 2. Register services

In `Program.cs`, compose the framework through the `AddCrestAppsCore(...)` builder:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddChatInteractions()
    )
);
```

`AddAISuite(...)` adds the shared CrestApps core services, the AI runtime, and orchestration together. If you prefer the lower-level registrations, the same features are still available as raw `IServiceCollection` extensions such as `AddCoreAIServices()` and `AddCoreAIOrchestration()`.

Then add only the host-specific pieces you need:

```csharp
builder.Services.AddCoreSignalR();
```

## 3. Configure connections and deployments

At minimum, provide a connection and a deployment through configuration or your own catalog/store implementation.

```json
{
  "CrestApps": {
    "AI": {
      "Connections": [
        {
          "Name": "primary-openai",
          "ClientName": "OpenAI",
          "ApiKey": "YOUR_API_KEY"
        }
      ],
      "Deployments": [
        {
          "Name": "gpt-4.1",
          "ClientName": "OpenAI",
          "Type": "Chat",
          "ModelName": "gpt-4.1"
        }
      ]
    }
  }
}
```

The default configuration sections are:

- `CrestApps:AI:Connections` for provider connections and credentials
- `CrestApps:AI:Deployments` for standalone deployments and deployment metadata

Keep connection credentials under `Connections` and define model/deployment choices under `Deployments`.

## Fastest path to a first prompt

For the smallest useful setup:

1. Register `AddAISuite(...)`
2. Add one provider like `AddOpenAI()`
3. Add `AddChatInteractions()`
4. Configure one connection in `CrestApps:AI:Connections`
5. Configure one chat deployment in `CrestApps:AI:Deployments`
6. Create an AI profile that points at that deployment
7. Use Chat Interactions as the first playground-style UI

That path gets you to a working chat experience with the fewest moving parts.

## 4. Pick the application model

The service registrations stay the same; only the UI or endpoint layer changes.

### MVC

Use MVC when you want server-rendered admin pages, controllers, and SignalR chat hubs. The reference implementation is **`src\Startup\CrestApps.Core.Mvc.Web`**.

Use these files as the primary sample:

- `src\Startup\CrestApps.Core.Mvc.Web\Program.cs` for service registration and feature composition
- `src\Startup\CrestApps.Core.Mvc.Web\appsettings.json` for connection and deployment examples

### Razor Pages

Use the same DI setup and inject framework services into page models:

```csharp
public sealed class ChatModel : PageModel
{
    private readonly IOrchestrator _orchestrator;

    public ChatModel(IOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }
}
```

### Blazor Server / Blazor Web App

Inject `IAICompletionService`, `IOrchestrator`, or `IAIClientFactory` into components and stream partial results to the UI. `AddCoreSignalR()` remains useful when you want hub-based browser communication outside normal Blazor circuits.

### Minimal APIs

Expose thin endpoints over the framework services:

```csharp
app.MapPost("/chat", async (IOrchestrator orchestrator, ChatRequest request) =>
{
    // Build context, execute orchestration, and return the result.
});
```

### .NET MAUI Hybrid / Blazor Hybrid

Register the same services in the app host and use them from native pages or hybrid components. This lets you share provider configuration, orchestration, tools, and document logic between desktop/mobile shells and your web hosts.

## 5. Persist state when needed

If your app needs durable profiles, sessions, templates, or connection records, plug in a store implementation. The repository includes two first-party options:

- `CrestApps.Core.Data.EntityCore` for Entity Framework Core-backed catalogs and stores
- `CrestApps.Core.Data.YesSql` for YesSql-backed catalogs and stores

Each feature has its own per-feature store registration extension so you can see exactly which stores each feature needs:

**Entity Framework Core** — configure the data store, then register stores per feature via the builder:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddEntityCoreStores()
        .AddOpenAI()
        .AddChatInteractions(ci => ci
            .AddEntityCoreStores()
        )
        .AddDocumentProcessing(dp => dp
            .AddEntityCoreStores()
            .AddOpenXml()
            .AddPdf()
        )
        .AddAIMemory(memory => memory
            .AddEntityCoreStores()
        )
        .AddA2AClient(a2a => a2a
            .AddEntityCoreStores()
        )
        .AddMcpClient(mcp => mcp
            .AddEntityCoreStores()
        )
        .AddMcpServer(mcpServer => mcpServer
            .AddEntityCoreStores()
        )
    )
    .AddIndexingServices(indexing => indexing
        .AddEntityCoreStores()
    )
    .AddEntityCoreSqliteDataStore(
        $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "App_Data", "crestapps.db")}")
);
```

**YesSql** — configure the data store, then register stores per feature via the builder:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddYesSqlStores()                        // AIProfile, AIProfileTemplate, connections, deployments, chat sessions
        .AddOpenAI()
        .AddChatInteractions(ci => ci
            .AddYesSqlStores()                    // ChatInteraction, IChatInteractionPromptStore
        )
        .AddDocumentProcessing(dp => dp
            .AddYesSqlStores()                    // IAIDocumentStore, IAIDocumentChunkStore, IAIDataSourceStore
            .AddOpenXml()
            .AddPdf()
        )
        .AddAIMemory(memory => memory
            .AddYesSqlStores()                    // IAIMemoryStore
        )
        .AddA2AClient(a2a => a2a
            .AddYesSqlStores()                    // A2AConnection
        )
        .AddMcpClient(mcp => mcp
            .AddYesSqlStores()                    // McpConnection
        )
        .AddMcpServer(mcpServer => mcpServer
            .AddYesSqlStores()                    // McpPrompt, McpResource
        )
    )
    .AddIndexingServices(indexing => indexing
        .AddYesSqlStores()                        // ISearchIndexProfileStore
    )
    .AddYesSqlDataStore(configuration => configuration
        .UseSqLite("Data Source=app.db;Cache=Shared")
    )
);
```

:::tip
Both Entity Framework Core and the built-in YesSql stores follow the same `IStoreCommitter` pattern. Register the MVC action filter, the Minimal API endpoint filter, and the existing SignalR store-committer filter when your store implementation uses a unit-of-work/session model. If your custom implementation persists immediately and does not stage tracked changes, you do not need `IStoreCommitter`.
:::

For MVC actions:

```csharp
builder.Services
    .AddControllersWithViews()
    .AddCrestAppsStoreCommitterFilter();
```

For Minimal APIs:

```csharp
app.MapGroup("/api")
    .AddEndpointFilter<StoreCommitterEndpointFilter>();
```

If you already use another ORM or storage model, implement the same catalog/store abstractions against your preferred backend. See [Data Storage](data-storage.md) for the full per-feature store reference.

## 6. Add features one layer at a time

The intended progression is:

1. `AddCrestAppsCore(builder => ...)` as the single CrestApps entrypoint
2. `AddAISuite(...)` for the shared foundation, AI runtime, and orchestration primitives
3. Provider registration inside the AI suite such as `AddOpenAI()`
4. Higher-level capabilities like chat, document processing, MCP, A2A, SignalR, templates, and custom tools

That layering keeps small apps lightweight while letting larger apps grow into a full AI platform without changing architectural direction.

## 7. Use the MVC sample as the reference host

`src\Startup\CrestApps.Core.Mvc.Web\Program.cs` is the canonical example for:

- configuration layering
- service registration order
- provider registration blocks
- storage registration
- MCP and A2A setup
- SignalR and chat integration
- feature-by-feature opt-in extensions

Use that sample when you want to add one capability at a time and compare a minimal setup with a full production-style composition.
