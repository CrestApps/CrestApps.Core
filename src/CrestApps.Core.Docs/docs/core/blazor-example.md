---
sidebar_label: Blazor Example
sidebar_position: 21
title: Blazor Example Application
description: Complete walkthrough of the CrestApps.Core.Blazor.Web example application showing how to bootstrap a full AI-powered Blazor Server application with EntityCore stores.
---

# Blazor Example Application

> A complete walkthrough of the `CrestApps.Core.Blazor.Web` example project that demonstrates every framework feature in a Blazor Server application using Entity Framework Core (EntityCore) stores.

## Application Structure

```text
CrestApps.Core.Blazor.Web/
├── Program.cs                     ← Full startup configuration (EntityCore stores)
├── Components/
│   ├── App.razor                  ← Root Blazor component
│   ├── Routes.razor               ← Router with auth
│   ├── Layout/
│   │   └── MainLayout.razor       ← Admin sidebar layout
│   ├── Pages/
│   │   ├── Home/                  ← Dashboard
│   │   ├── Account/               ← Login, AccessDenied
│   │   ├── Admin/                 ← Articles CRUD, Settings
│   │   ├── AI/                    ← Connections, Deployments, Profiles, Templates
│   │   ├── AIChat/                ← AI chat sessions, analytics
│   │   ├── ChatInteractions/      ← Interactive chat testing
│   │   ├── A2A/                   ← A2A connection management
│   │   ├── Mcp/                   ← MCP connections, prompts, resources
│   │   ├── DataSources/           ← Data source management
│   │   └── Indexing/              ← Index profile management
│   └── Shared/                    ← Pager, AlertMessage components
├── Areas/                         ← Services, models, hubs, endpoints (non-UI)
├── Controllers/                   ← AccountController for cookie auth
├── Services/                      ← SiteSettingsStore, BlazorAppDbContext, etc.
├── Tools/                         ← Custom AI tools
├── App_Data/                      ← Runtime data (EF Core SQLite DB)
└── wwwroot/                       ← Static files
```

The Blazor sample mirrors the same feature areas as the MVC example but uses Razor components with `@code` blocks instead of controllers and Razor views. Non-UI concerns such as services, models, SignalR hubs, and API endpoints remain under `Areas/` and `Services/`.

## Key Differences from MVC

| Concern | MVC Example | Blazor Example |
|---------|------------|----------------|
| **Rendering** | Controllers + Razor Views | Blazor Server (`InteractiveServerRenderMode`) with Razor components |
| **Data stores** | YesSql (document store + SQLite) | EntityCore (EF Core + SQLite) |
| **Forms** | HTML forms with tag helpers | `EditForm` with `InputText`, `InputSelect`, `InputNumber` |
| **Navigation** | `RedirectToAction()` | `NavigationManager.NavigateTo()` |
| **Auth flow** | Cookie auth in MVC controllers | Cookie auth still uses `AccountController` for form POST; Blazor pages use `[Authorize]` |
| **App-specific data** | YesSql collections and indexes | `BlazorAppDbContext` for articles, analytics, and app-owned tables |
| **Real-time chat** | SignalR hubs with JS interop | SignalR client via `HubConnectionBuilder` in Blazor components |
| **Shared components** | Partial views and tag helpers | Reusable Razor components (`Pager`, `AlertMessage`, etc.) |

## Startup Configuration Walkthrough

The `Program.cs` follows the same numbered-section pattern as the MVC example, with key differences for Blazor and EntityCore.

### Section 1 — Logging

Same as the MVC example: NLog with daily log file rotation in `App_Data/logs/`.

### Section 2 — Application Configuration

Same layered configuration pattern: `appsettings.json` → `appsettings.{Environment}.json` → `App_Data/appsettings.json` as the highest-priority local override. `SiteSettingsStore` manages mutable admin settings backed by `App_Data/site-settings.json`.

### Section 3 — ASP.NET Core Blazor Setup

Instead of `AddControllersWithViews()`, the Blazor host registers:

- `AddRazorComponents().AddInteractiveServerComponents()` for Blazor Server rendering
- `AddSignalR()` with camelCase JSON payloads
- `AddControllersWithViews()` is still registered for the `AccountController` cookie-auth endpoints

### Section 4 — Authentication & Authorization

Same cookie-based authentication with an `"Admin"` authorization policy requiring the Administrator role. The `AccountController` handles login/logout form POSTs because Blazor Server cannot issue HTTP redirects with set-cookie headers directly.

### Section 5 — CrestApps Foundation + AI Services (EntityCore)

The core framework registration chain uses EntityCore stores instead of YesSql:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddEntityCoreStores()
        .AddMarkdown()
        .AddCopilotOrchestrator()
        .AddChatInteractions(chat => chat
            .AddEntityCoreStores()
            .ConfigureChatHubOptions<ChatInteractionHub>()
        )
        .AddDocumentProcessing(documentProcessing => documentProcessing
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
            .AddFtpResources()
            .AddSftpResources()
        )
        .AddSignalR(addStoreCommitterFilter: true)
        .AddA2AHost()
        .AddOpenAI()
        .AddAzureOpenAI()
        .AddOllama()
        .AddAzureAIInference()
    )
    .AddIndexingServices(indexing => indexing
        .AddEntityCoreStores()
        .AddElasticsearch(/* ... */)
        .AddAzureAISearch(/* ... */)
    )
    .AddEntityCoreSqliteDataStore("Data Source=App_Data/crestapps.db")
);
```

Every feature builder calls `.AddEntityCoreStores()` instead of `.AddYesSqlStores()`. The root builder calls `.AddEntityCoreSqliteDataStore(...)` (which maps to `AddCoreEntityCoreSqliteDataStore(...)`) instead of `.AddYesSqlDataStore(...)`.

Because EntityCore commits each write immediately via `SaveChangesAsync()`, there is no request-level unit-of-work middleware. This is one fewer piece of infrastructure compared to YesSql hosts.

### Section 6 — Schema Initialization

At startup, the Blazor host calls `InitializeEntityCoreSchemaAsync()` to apply EF Core migrations and ensure the SQLite database schema is current. This replaces the YesSql table-creation and index-registration step used in the MVC example.

### Sections 7–13

The remaining sections (providers, Elasticsearch, Azure AI Search, MCP, custom tools, background tasks, and middleware pipeline) follow the same structure as the MVC example. The middleware pipeline adds:

- `MapRazorComponents<App>().AddInteractiveServerRenderMode()` instead of MVC route patterns
- MVC controller routes are still mapped for `AccountController`
- SignalR hub endpoints remain at `/hubs/ai-chat` and `/hubs/chat-interaction`
- MCP SSE endpoint at `/mcp`

## Running the Application

**Visual Studio:** Set `CrestApps.Core.Blazor.Web` as the startup project and press **F5** (or **Ctrl+F5** to run without debugging).

**Command line:**

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Blazor.Web\CrestApps.Core.Blazor.Web.csproj
```

The application starts on `https://localhost:5200`. Default login credentials are **admin** / **admin**.

Configure AI provider connections in `App_Data/appsettings.json` before using AI features.

**Aspire integration:** The Blazor sample is included in the `CrestApps.Core.Aspire.AppHost` project alongside the MVC sample, so `dotnet run` on the AppHost boots both applications together.

## Feature Parity

The Blazor example replicates all MVC example features:

- **All 19 feature areas** with full CRUD pages (AI Connections, Deployments, Profiles, Templates, Chat Interactions, A2A, MCP, Data Sources, Indexing, Articles, Settings, and more)
- **Same sidebar navigation**, UI layout, and field grouping
- **SignalR real-time chat** for AI Chat and Chat Interactions using `HubConnectionBuilder` in Blazor components
- **Analytics dashboards** with filtering and CSV export
- **Admin settings** with all 8 configuration sections (General, AI, Chat, Speech, Admin Widget, Connections, Deployments, and Data Sources)

## Key Takeaways

1. **EntityCore is a drop-in replacement for YesSql** — swap `.AddYesSqlStores()` for `.AddEntityCoreStores()` and `.AddYesSqlDataStore(...)` for `.AddEntityCoreSqliteDataStore(...)`
2. **Blazor Server works seamlessly** — the framework's DI-first design means the same services power both MVC and Blazor hosts
3. **Cookie auth bridges the gap** — `AccountController` handles login/logout POSTs while Blazor pages use `[Authorize]` and `AuthenticationStateProvider`
4. **No unit-of-work middleware needed** — EntityCore commits immediately, simplifying the middleware pipeline
5. **Same feature set, different UI model** — choose MVC or Blazor based on your team's preference; the framework layer is identical
