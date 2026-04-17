---
title: Architecture & Dependencies
sidebar_position: 2
---

# Architecture & Dependency Diagram

This page describes the project architecture and how the major layers depend on each other.

## Dependency Diagram

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ Application hosts                                                            │
│                                                                              │
│  CrestApps.Core.Mvc.Web   Aspire AppHost   Custom MVC / Razor / Blazor app   │
└───────────────────────────────┬──────────────────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ Core foundation                                                              │
│                                                                              │
│  CrestApps.Core                 CrestApps.Core.Abstractions                  │
│  CrestApps.Core.Infrastructure  CrestApps.Core.Infrastructure.Abstractions   │
└───────────────────────────────┬──────────────────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ AI runtime and feature packages                                              │
│                                                                              │
│  AI runtime   Chat   A2A   MCP   SignalR   Templates   Copilot   Markdown    │
│  Azure utilities                                                             │
└───────────────────────────────┬──────────────────────────────────────────────┘
                                │
                ┌───────────────┴────────────────┬─────────────────────────────┐
                ▼                                ▼                             ▼
┌──────────────────────────────┐  ┌──────────────────────────────┐  ┌──────────────────────────────┐
│ Provider integrations        │  │ Search and data sources      │  │ Storage implementations      │
│                              │  │                              │  │                              │
│ OpenAI / Azure OpenAI        │  │ Azure AI Search              │  │ Entity Framework Core        │
│ Ollama / Azure AI Inference  │  │ Elasticsearch                │  │ YesSql                       │
│ PDF / OpenXml / FTP / SFTP   │  │                              │  │                              │
└──────────────────────────────┘  └──────────────────────────────┘  └──────────────────────────────┘
```

## Layer Descriptions

| Project | Role |
|---------|------|
| `CrestApps.Core.Abstractions` | Core interfaces: `ICatalog<T>`, `INamedEntity`, `ExtensibleEntity`, `IODataValidator` |
| `CrestApps.Core.AI.Abstractions` | AI interfaces: `IAICompletionService`, `IAIProfileManager`, `IOrchestrator`, models |
| `CrestApps.Core` | Default implementations of core abstractions, `IServiceCollection` extensions |
| `CrestApps.Core.AI` | AI orchestration, `DefaultOrchestrator`, tool execution, completion services |
| `CrestApps.Core.AI.Chat` | Chat session management, prompt storage, `IAIChatSessionManager` |
| `CrestApps.Core.AI.Documents` | Document ingestion, uploaded-file storage abstraction, document tools, and document RAG |
| `CrestApps.Core.AI.OpenAI` | OpenAI provider (`ChatClient`, streaming, tool calls) |
| `CrestApps.Core.AI.OpenAI.Azure` | Azure OpenAI provider with data source integration |
| `CrestApps.Core.AI.Ollama` | Ollama provider for locally hosted LLMs |
| `CrestApps.Core.AI.AzureAIInference` | Azure AI Inference / GitHub Models provider |
| `CrestApps.Core.AI.Copilot` | GitHub Copilot chat orchestration, OAuth flow, credential management |
| `CrestApps.Core.Azure.AISearch` | Azure AI Search provider primitives for client setup, index management, document management, and OData filters |
| `CrestApps.Core.AI.AISearch` | Azure AI Search integration for AI document index profiles, AI memory search, and AI data-source registrations |
| `CrestApps.Core.Elasticsearch` | Elasticsearch provider primitives for client setup, index management, document management, and query/filter translation |
| `CrestApps.Core.AI.Elasticsearch` | Elasticsearch integration for AI document index profiles, AI memory search, and AI data-source registrations |
| `CrestApps.Core.AI.Mcp` | Model Context Protocol (MCP) client and server |
| `CrestApps.Core.AI.Mcp.Ftp` | FTP/FTPS MCP resource type handler |
| `CrestApps.Core.AI.Mcp.Sftp` | SFTP MCP resource type handler |
| `CrestApps.Core.Azure` | Azure-specific utilities and integration helpers |
| `CrestApps.Core.SignalR` | SignalR hub abstractions for real-time AI chat |
| `CrestApps.Core.Support` | General utility classes |
| `CrestApps.Core.Templates` | Prompt template engine |

### Storage layer

| Project | Role |
|---------|------|
| `CrestApps.Core.Data.EntityCore` | Entity Framework Core-based catalog and store implementation |
| `CrestApps.Core.Data.YesSql` | YesSql-based document catalog implementation (SQLite, PostgreSQL, SQL Server) |

### Application layer

| Project | Role |
|---------|------|
| `CrestApps.Core.Mvc.Web` | Standalone ASP.NET Core MVC application with full admin UI |
| Blazor / Other | Future: Blazor Server/WASM, minimal APIs, etc. |

## Data Flow

```
User → UI (MVC/Blazor/OC) → SignalR Hub → Orchestrator → AI Provider → LLM
                                  ↓                              ↑
                          Session Manager ←──── Prompt Store ────┘
                                  ↓
                          YesSql / Custom Store
```

1. **User** sends a message via the UI (browser)
2. **SignalR Hub** receives the message and resolves the AI profile
3. **Orchestrator** builds the conversation context (system prompt, history, tools)
4. **AI Provider** (OpenAI, Azure, Ollama) streams the response
5. **Prompt Store** persists both user and assistant messages
6. **SignalR Hub** streams response chunks back to the client

## Extensibility Points

| Interface | Purpose | Default Implementation |
|-----------|---------|----------------------|
| `IAICompletionService` | AI provider abstraction | OpenAI, Azure OpenAI, Ollama |
| `IOrchestrator` | Controls AI request pipeline | `DefaultOrchestrator` |
| `ICatalog<T>` | CRUD for named entities | `NamedSourceDocumentCatalog<T>` (YesSql) |
| `IAIProfileManager` | Profile CRUD | Module-specific implementations |
| `IAIChatSessionManager` | Session lifecycle | YesSql-based implementation |
| `IAIChatSessionPromptStore` | Prompt persistence | YesSql-based implementation |
| `ICatalogEntryHandler<T>` | Entity lifecycle hooks | Per-provider handlers |
