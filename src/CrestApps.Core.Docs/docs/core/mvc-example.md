---
sidebar_label: MVC Example
sidebar_position: 20
title: MVC Example Application
description: Complete walkthrough of the CrestApps.Core.Mvc.Web example application showing how to bootstrap a full AI-powered MVC application.
---

# MVC Example Application

> A complete walkthrough of the `CrestApps.Core.Mvc.Web` example project that demonstrates every framework feature in a standard ASP.NET Core MVC application.


## Application Structure

```text
CrestApps.Core.Mvc.Web/
├── Program.cs                  ← Full startup configuration
├── Areas/
│   ├── Admin/                  ← Settings, articles, and shared admin-only pages
│   ├── AI/                     ← AI connections, deployments, profiles, templates
│   ├── AIChat/                 ← AI chat sessions and Copilot auth
│   ├── ChatInteractions/       ← Interactive chat workflows
│   ├── A2A/                    ← A2A host management
│   ├── Mcp/                    ← MCP hosts, prompts, and resources
│   ├── DataSources/            ← Data source CRUD and storage
│   └── Indexing/               ← Index profiles and AI document indexing
├── BackgroundTasks/            ← Hosted services for maintenance
├── Controllers/                ← Non-area MVC controllers such as Home and Account
├── Hubs/                       ← SignalR hubs for real-time chat
├── Indexes/                    ← YesSql index providers
├── Tools/                      ← Custom AI tools
├── Views/                      ← Non-area Razor views
├── App_Data/                   ← Runtime data (DB, logs, documents, settings)
└── wwwroot/                    ← Static files
```

The sample now keeps feature-specific controllers, Razor views, and related MVC-only services or models close to the owning area instead of accumulating under a single `Areas/Admin` catch-all folder.

MVC admin forms also keep placeholder dropdown options in the Razor views instead of injecting fake empty `SelectListItem` entries from controllers. The option collections now contain only real persisted values, while the views render plain placeholders such as `Select provider`, `Use default orchestrator`, or `None` when an empty selection is allowed.

The MVC admin AI settings screen now exposes the shared `GeneralAISettings` values used by the framework runtime, including preemptive memory retrieval. It also now stores the site-wide Chat Interactions chat mode and resolves the default text-to-speech voice from the selected speech deployment instead of relying on free-form voice IDs. Profile editors also expose Orchard-style chat mode selection plus a deployment-driven voice picker, while the MVC AI Chat page, Chat Interactions page, and admin widget honor those chat mode settings for text input, microphone dictation, and conversation mode when speech deployments are configured. Speech input now prefers the browser's full locale (for example `en-US`) and the shared Azure Speech client normalizes neutral culture names such as `en` before sending speech requests. Conversation mode now also keeps the same start/end button treatment across MVC AI Chat and Chat Interactions, hides the Chat Interactions mic/send controls while live conversation mode is active, streams speech chunks more frequently to reduce transcription latency, auto-dismisses the conversation-ended notice after a short delay, and limits automatic spoken playback to turns that were generated while live conversation mode was actually active.

The MVC sample now also reuses the same Orchard-style drag-and-drop knowledge upload treatment in the **AI Profile**, **Chat Interaction**, and **AI Template** editors. Profile-source templates can upload and remove template documents directly from the MVC admin UI, and those stored template documents are cloned into new AI Profiles when the template is applied during profile creation. The MVC Chat Interactions sidebar now validates number inputs such as **Strictness** against each field's `min`/`max` attributes before autosaving, marks invalid fields inline, and keeps the existing `Saved ✓` feedback when the save succeeds.

Because the MVC sample stores runtime state under `App_Data`, the project now excludes `App_Data/**` from `.NET 10` `dotnet watch` input discovery. That prevents Aspire and other watch-based local runs from restarting `MvcWeb` when uploads create files under `App_Data/Documents` or runtime services update logs and local data files.

## Startup Configuration Walkthrough

The `Program.cs` file is organized into numbered sections. Here is what each section does:

### Section 1 — Logging

Configures NLog with daily log file rotation in `App_Data/logs/`. Replaceable with Serilog, Application Insights, or any logging provider.

### Section 2 — Application Configuration

Loads settings from the normal appsettings chain plus `App_Data/appsettings.json` as the highest-priority local override file with automatic reload-on-change:

| Service | Purpose |
|---------|---------|
| `App_Data/appsettings.json` | Local machine overrides for infrastructure settings (AI connections, credentials, Elasticsearch, Azure AI Search) |
| `App_Data/site-settings.json` | Mutable admin-managed settings (AI options, deployments, chat, admin widget, etc.) owned exclusively by `SiteSettingsStore` — not registered in the configuration pipeline |
| `SiteSettingsStore` | In-memory store for admin-managed settings backed by `site-settings.json`. Reads from memory via `Get<T>()`, writes via `Set<T>()`, and persists atomically via `SaveChangesAsync()` |

The project file also excludes the broader `App_Data` folder from `dotnet watch` so watch-based local hosts do not mistake uploaded documents or SQLite/log writes for source changes and restart the app in the middle of a request.

### Section 3 — ASP.NET Core MVC Setup

Registers the standard host services first so the file starts with familiar ASP.NET Core concerns before any CrestApps-specific registrations:

- `AddLocalization()`
- `AddControllersWithViews()`
- `AddSignalR()` with camelCase JSON payloads

### Section 4 — Authentication & Authorization

Cookie-based authentication with an `"Admin"` authorization policy requiring the Administrator role.



### Section 5 — CrestApps Foundation + AI Services

The core framework registration chain:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddMarkdown()
        .AddClaudeOrchestrator()
        .AddCopilotOrchestrator()
        .AddChatInteractions(chat => chat.ConfigureChatHubOptions<ChatInteractionHub>())
        .AddDocumentProcessing(documentProcessing => documentProcessing
            .AddOpenXml()
            .AddPdf()
        )
        .AddAIMemory()
        .AddA2AClient()
        .AddMcpClient()
        .AddMcpServer(mcpServer => mcpServer
            .AddYesSqlStores()
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
        .AddYesSqlStores()
        .AddElasticsearch(configuration.GetSection("CrestApps:Elasticsearch"), elasticsearch => elasticsearch
            .AddAIDocuments()
            .AddAIDataSources()
            .AddAIMemory()
        )
        .AddAzureAISearch(configuration.GetSection("CrestApps:AzureAISearch"), azureAISearch => azureAISearch
            .AddAIDocuments()
            .AddAIDataSources()
            .AddAIMemory()
        )
    )
    .AddYesSqlDataStore(appDataPath)
);
```

`AddAISuite(...)` always wires the shared foundation, AI runtime, and orchestration together. `AddChatInteractions()` inside that suite then registers the shared `DataSourceChatInteractionSettingsHandler`, so Chat Interactions persist the selected data source and RAG metadata through the framework settings pipeline instead of MVC-only wiring. The provider service blocks also pull in the shared data-source RAG registrations, which register both `DataSourceOrchestrationHandler` and `DataSourcePreemptiveRagHandler` at the framework level so source availability instructions and preemptive RAG stay aligned with the saved chat settings.

`AddAIDocuments()`, `AddAIDataSources()`, and `AddAIMemory()` in those indexing blocks now come from the AI-specific provider packages: `CrestApps.Core.AI.Elasticsearch` and `CrestApps.Core.AI.Azure.AISearch`. The base `CrestApps.Core.Elasticsearch` and `CrestApps.Core.Azure.AISearch` packages now stay focused on the provider primitives and shared search infrastructure only.

The MVC sample now also registers both the **Claude** and **Copilot** orchestrators. Claude uses the official Anthropic SDK with a site-level authentication mode, API key, and live model discovery, while Copilot keeps its dedicated OAuth/BYOK flow. Admins can choose either orchestrator from the same AI Profile, AI Template, and Chat Interaction editors.

Documents, memory, and data sources now remain fully independent orchestration sources in the shared framework. Each source injects its own availability instructions and preemptive-RAG context, so the orchestrator can compose them together without the document prompts needing to know whether memory or data sources are also attached.

The MVC sample explicitly calls `AddMarkdown()` inside `AddAISuite(...)`. That keeps Markdown-aware normalization opt-in at the host level instead of making `CrestApps.Core.AI` depend on the Markdig-backed package automatically.

### Section 6 — AI Clients

Registers all supported AI providers:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddAzureOpenAI()
        .AddOllama()
        .AddAzureAIInference()
    )
);
```

The MVC sample is configured to use the recommended standalone settings by default: `CrestApps:AI:Connections` for shared connection records and `CrestApps:AI:Deployments` for deployment metadata.

`AddCoreAIAzureOpenAI()` also registers the `AzureSpeech` deployment provider used by MVC speech-to-text and text-to-speech selectors, so Azure AI Services deployments from `CrestApps:AI:Deployments` participate in the same merged deployment catalog as UI-managed deployments.

The MVC runtime reads connection definitions from `CrestApps:AI:Connections` and UI-managed connection records from the store into one merged connection catalog. The deployment catalog layers together UI-managed typed deployments and `CrestApps:AI:Deployments` entries from every configured section. Those deployment entries can either reference a shared `ConnectionName` or carry contained-connection settings directly. That means dropdowns, deployment resolution, and connection resolution all see the same unified set without an app restart.

Both merged catalogs also expose configurable section lists through `AIProviderConnectionCatalogOptions` and `AIDeploymentCatalogOptions`, so a host can append additional configuration paths without replacing the MVC/UI store integration. The MVC sample intentionally keeps its default connection discovery focused on `CrestApps:AI:Connections` so the local sample demonstrates the recommended standalone configuration layout, while deployment discovery reads `CrestApps:AI:Deployments`.

```json
{
  "CrestApps": {
    "AI": {
      "Connections": [
        {
          "Name": "WinnerWare",
          "ClientName": "Azure",
          "Endpoint": "https://winnerwareai.openai.azure.com/",
          "AuthenticationType": "ApiKey",
          "ApiKey": "YOUR_API_KEY",
          "DisplayText": "WinnerWare Azure OpenAI"
        }
      ]
    }
  }
}
```

The framework can still be configured to read additional provider-grouped connection sections when a host explicitly opts into them, but the MVC sample does not enable those paths by default. Connection settings only describe the AI connection itself; deployment names and types belong in `CrestApps:AI:Deployments` or in the UI deployment editor, and config deployments can optionally point back to a shared connection by setting `ConnectionName`.


```json
{
  "CrestApps": {
    "AI": {
      "Deployments": [
        {
          "ClientName": "AzureSpeech",
          "Name": "whisper",
          "Type": "SpeechToText",
          "Endpoint": "https://eastus.stt.speech.microsoft.com",
          "AuthenticationType": "ApiKey",
          "ApiKey": "YOUR_API_KEY"
        },
        {
          "ClientName": "AzureSpeech",
          "Name": "AzureTextToSpeech",
          "Type": "TextToSpeech",
          "Endpoint": "https://eastus.tts.speech.microsoft.com",
          "AuthenticationType": "ApiKey",
          "ApiKey": "YOUR_API_KEY"
        }
      ]
    }
  }
}
```

When a connection or deployment comes from system configuration, the MVC admin keeps it visible in the same listing as user-defined records, marks it read-only, and blocks edit/delete actions. Only records created through the UI remain editable there. Names are also enforced across both sources: MVC rejects duplicate UI names, and if appsettings and the store define the same connection or deployment name, the UI/store record wins and the conflicting configuration record is skipped.

### Section 7 — Elasticsearch Services

Keeps the Elasticsearch registrations together so you can remove that provider by deleting a single block:

```csharp
builder.Services
    .AddCoreElasticsearchServices(

        builder.Configuration.GetSection("CrestApps:Elasticsearch"))
    .AddElasticsearchAIDocumentSource()
    .AddElasticsearchAIDataSource()
    .AddElasticsearchAIMemorySource()
    .AddElasticsearchArticleSource();

```

When users create MVC index profiles, `AI Documents`, `AI Memory`, and `Data Source` profiles must select an embedding deployment, while `Articles` hides that selector entirely. That validation now runs through source-specific `IIndexProfileHandler` implementations registered by provider-owned extensions such as `AddElasticsearchAIDocumentSource()` and `AddAzureAISearchAIMemorySource()`, so each provider/type pair owns its own embedding requirements and field schema.

The MVC sample provisions the remote index during profile creation by resolving the keyed `ISearchIndexManager` for the selected provider, composing `IndexFullName` from the provider's configured `IndexPrefix` plus the user-entered index name, rejecting the create when that remote index already exists, and only persisting the local profile after the remote index is created successfully.

After creation, the MVC admin keeps the index name, provider, type, and embedding deployment immutable so the remote index contract cannot drift from the saved profile.

### Section 8 — Azure AI Search Services

Mirrors the Elasticsearch block so Azure AI Search is equally easy to enable or remove:

```csharp
builder.Services

    .AddCoreAzureAISearchServices(

        builder.Configuration.GetSection("CrestApps:AzureAISearch"))

    .AddAzureAISearchAIDocumentSource()
    .AddAzureAISearchAIDataSource()
    .AddAzureAISearchAIMemorySource()
    .AddAzureAISearchArticleSource();
```

Deleting an MVC index profile now also deletes the remote Elasticsearch or Azure AI Search index through the keyed `ISearchIndexManager` registered for that provider, preventing orphaned indexes from lingering after the profile is removed. The same handler pipeline is reused for synchronization and type-specific validation so the controller stays focused on the Orchard-style CRUD flow.

If an administrator already deleted the remote index directly in Elasticsearch or Azure AI Search, the MVC app now still allows deleting the local index profile. The same local delete is also allowed when the stored profile no longer has a resolvable remote index name or the original provider registration is no longer available. The delete flow only blocks local removal when the remote index still exists and the provider fails to delete it.

`Articles` remains the only MVC-specific source registration. The sample app adds that descriptor directly in `Program.cs` and pairs it with `ArticleIndexProfileHandler`, because the article catalog and indexing logic belong only to the MVC sample rather than the reusable provider packages.




The MVC admin chat widget now stays bound to the configured admin-chat profile instead of exposing a profile picker, restores its open/closed state and active session across page navigation, and reuses the stored session automatically when the next admin page loads. **Settings → AI Settings** now includes an **Admin widget** card where administrators choose that profile; leaving it empty disables the widget entirely. The same card also lets administrators change the widget accent color, which now defaults to the admin theme secondary color (`#6c757d`) instead of a hard-coded green. The widget now boots a real chat session immediately, so profiles with an **Initial prompt** show that assistant message first; otherwise it falls back to the welcome message and then **What do you want to know?** when no welcome text is configured. The shared widget runtime also now lets users drag both the floating toggle button and the widget shell, resize the widget, restore the default size from the header, and persist that layout in browser storage unless a host opts out through the widget config.

The MVC sample also now records provider usage in a dedicated **AI Usage Analytics** report. The report groups tracked completion calls by authenticated username or **Anonymous**, then breaks usage down by completion client and resolved model/deployment while showing total calls, distinct sessions, distinct chat interactions, token totals, and average latency. Session analytics now keep token totals and user-visible response latency separate so the main chat analytics page still shows per-session performance while the usage report captures provider activity more directly.

`Program.cs` also registers a sample `sendEmail` AI function in the MVC host. The sample tool does not deliver real mail — it logs the requested recipient, subject, and message so you can see how host-specific tools plug into the shared framework.

### Section 9 — Model Context Protocol (MCP)

Full bidirectional MCP setup:

- **Client**: `AddMcpClient()` for connecting to remote MCP servers (`McpConnection` catalog)

- **Server**: `AddMcpServer(...)` with `.AddYesSqlStores()` for `McpPrompt` and `McpResource` catalogs, plus `.AddFtpResources()`, `.AddSftpResources()`, and `MapMcp("mcp")` handlers for tools, prompts, and resources

The MCP server endpoint at `/mcp` includes configurable authentication middleware supporting API key, cookie auth, and admin role checks.

### Section 10 — Custom AI Tools

Registers application-specific tools:

```csharp
builder.Services
    .AddCoreAITool<CalculatorTool>(CalculatorTool.TheName)
        .WithTitle("Calculator")
        .WithDescription("Performs basic arithmetic.")

        .WithCategory("Utilities")

        .Selectable();

builder.Services
    .AddCoreAITool<DataSourceSearchTool>(DataSourceSearchTool.TheName)
        .WithPurpose(AIToolPurposes.DataSourceSearch);
```

### Section 11 — Data Store (YesSql + SQLite sample)

The sample host configures YesSql with SQLite for persistent storage:

- Creates the SQLite database in `App_Data/crestapps.db`
- Reuses the shared `builder.Services.AddCoreYesSqlDataStore(...)` bootstrap from `CrestApps.Core.Data.YesSql`
- Registers 17 index providers for all framework models
- Registers catalog services for each model type
- Sets up manager and store implementations
- Lets the host flush the YesSql session at the request boundary through `YesSqlUnitOfWorkMiddleware` instead of asking repositories to commit themselves

The framework now also ships `CrestApps.Core.Data.EntityCore` as a first-party EF Core alternative. The MVC sample stays on YesSql so it can continue demonstrating the document-store flow and YesSql index providers, but other hosts can swap in the EntityCore package or a custom store implementation.

## Validation Feedback

The shared MVC layout now renders a Bootstrap validation summary whenever a request returns with invalid `ModelState`, so CRUD pages consistently show server-side validation errors even when the controller adds them dynamically. The same shared layout also surfaces `TempData["ErrorMessage"]` as a top-level alert for redirected error flows such as a failed remote index delete.

## Area Layout

The sample admin UI is intentionally split by feature so each area is easy to remove or understand in isolation:

| Area | Responsibility |
|------|----------------|
| `Admin` | Shared settings, articles, and general admin navigation |
| `AI` | AI connections, deployments, profiles, and templates |
| `AIChat` | Session-based AI chat plus Copilot OAuth callbacks |
| `ChatInteractions` | Long-lived interactive chat experiences |
| `A2A` | A2A host connections |
| `Mcp` | MCP host connections, prompts, and resources |
| `DataSources` | AI data sources and their MVC-specific store implementation |
| `Indexing` | Search index profiles, AI documents, and MVC indexing services |

### Section 12 — Background Tasks

Three hosted services for ongoing maintenance:

| Service | Purpose |
|---------|---------|
| `AIChatSessionCloseBackgroundService` | Runs every 5 minutes to close idle/expired chat sessions and trigger post-session/reporting work |
| `DataSourceSyncBackgroundService` | Synchronizes vector search data |
| `DataSourceAlignmentBackgroundService` | Aligns indices after config changes |

The AI chat close service now also keeps the MVC chat analytics and extracted-data reports aligned with closed sessions, while the data-source hosted services treat timer cancellation as a normal shutdown path and the alignment service safely handles an empty data-source store instead of dereferencing a null collection during a periodic run.






### Section 13 — Middleware Pipeline

The middleware pipeline includes:

1. Exception handling and HSTS
2. HTTPS redirection and static files
3. Routing, authentication, and authorization
4. MCP authentication middleware (conditional on `/mcp` path)
5. SignalR hub endpoints (`/hubs/ai-chat`, `/hubs/chat-interaction`)
6. MCP SSE endpoint (`/mcp`)
7. MVC route patterns

## Key Takeaways

1. **Each framework feature is a single extension method call** — compose only what you need
2. **Providers are independent** — register only the ones you use
3. **Storage is pluggable** — the YesSql section can be replaced entirely
4. **Configuration supports local overrides** — `App_Data/appsettings.json` sits on top of `appsettings.json` and `appsettings.{Environment}.json`
5. **MCP and A2A are optional** — add them only if you need protocol interop
6. **Background tasks handle maintenance** — no manual cleanup needed

## Running the Example

**Visual Studio:** Set `CrestApps.Core.Mvc.Web` as the startup project and press **F5** (or **Ctrl+F5** to run without debugging).

**Command line:**

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Mvc.Web\CrestApps.Core.Mvc.Web.csproj
```

The MVC sample resolves its content root to the project directory automatically, so you can run this command from the repository root without breaking view, static-file, or `App_Data` discovery.

The application starts on `https://localhost:5001`. Configure AI provider connections in `App_Data/appsettings.json` before using AI features.
