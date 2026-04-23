---
sidebar_label: Overview
sidebar_position: 1
title: Core Overview
description: The package layout and feature map for the standalone CrestApps.Core framework.
---

# Core Overview

`CrestApps.Core` is the framework layer that sits between raw provider SDKs and your application. It gives you one registration model, one catalog model, and one orchestration surface across chat, documents, retrieval, memory, protocol integration, and custom tools.

## What makes it valuable

- **Faster adoption** - integrate advanced AI capabilities without rebuilding the supporting infrastructure
- **Cleaner architecture** - keep prompts, profiles, tools, handlers, and defaults reusable instead of scattering them across the app
- **Provider flexibility** - switch models and providers without rewriting application logic
- **Business-ready workflows** - support lead generation, support automation, reporting, document analysis, and custom workflows
- **State-of-the-art integration** - use modern protocols such as MCP and A2A with a .NET-first developer experience

## Core capabilities

| Capability | What it enables |
| --- | --- |
| AI management | Connections, deployments, agent profiles, data sources, templates, and runtime configuration |
| Reusable AI agent profiles | Predefined behavior, prompts, model settings, tools, and retrieval rules for every session |
| Chat interactions | Provider-agnostic chat playgrounds and production chat experiences |
| Documents and knowledge | Upload files for summarization, extraction, tabulation, Q&A, and retrieval |
| RAG | Blend attached documents, data sources, and user memory with configurable preemptive retrieval |
| AI agents | Create specialized agents and coordinate work between them |
| MCP and A2A | Expose capabilities, connect to remote systems, and participate in protocol-driven ecosystems |
| Extensibility | Add custom AI functions, stores, handlers, authorization, templates, and runtime behavior |
| Metrics and reporting | Track chat activity, consumption, lead workflows, and post-session outcomes |
| Memory and personalization | Build assistants that remember durable user context over time |

## Recommended build order

If you are new to the repository, read the docs in this order:

1. [Getting Started](../getting-started.md)
2. [ASP.NET Core Integration](./getting-started-aspnet.md)
3. [Sample Projects](./sample-projects.md)
4. [AI Core](./ai-core.md)
5. The feature or provider pages you need next

## Quick start

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddChatInteractions()
    )
);
```

That is enough to start resolving `IAICompletionService`, `IAIClientFactory`, or `IOrchestrator` from DI and composing your own AI experience. Add storage, chat, documents, MCP, A2A, and indexing only when your host needs them.

By default:

- connections are loaded from `CrestApps:AI:Connections`
- deployments are loaded from `CrestApps:AI:Deployments`

The quickest way to validate the setup is to use **Chat Interactions** first, then create an [AI Profile](./ai-profiles.md) when you want reusable chat, agent, or orchestration behavior.

## Package map

| Area | Main package | Purpose |
| --- | --- | --- |
| Foundation | `CrestApps.Core` | Shared models, validation, catalog helpers, and host utilities |
| AI runtime | `CrestApps.Core.AI` | Deployments, profiles, completions, orchestration, tools, and memory |
| Chat | `CrestApps.Core.AI.Chat` | Chat sessions, widgets, handlers, and metrics |
| Documents | `CrestApps.Core.AI.Documents` | Uploaded-document ingestion, processing, storage abstractions, and document RAG |
| Templates | `CrestApps.Core.Templates` | Reusable prompts and template-driven profile composition |
| Providers | Provider packages | OpenAI, Azure OpenAI, Azure AI Inference, and Ollama integrations |
| Protocols | `CrestApps.Core.AI.Mcp`, `CrestApps.Core.AI.A2A` | MCP and A2A client/server building blocks |
| Persistence | `CrestApps.Core.Data.EntityCore`, `CrestApps.Core.Data.YesSql` | First-party persistence implementations for the shared catalog surface |
| Sample hosts | `CrestApps.Core.Mvc.Web`, `CrestApps.Core.Blazor.Web`, `CrestApps.Core.Aspire.AppHost` | Runnable reference hosts and composed local environment |
| Sample clients | `CrestApps.Core.Mvc.Samples.A2AClient`, `CrestApps.Core.Mvc.Samples.McpClient` | Small protocol-focused sample applications |

## Feature map

| Feature | Extension method | Package | Learn more |
| --- | --- | --- | --- |
| Builder entrypoint | `AddCrestAppsCore(builder => ...)` | `CrestApps.Core` | [ASP.NET Core integration](./getting-started-aspnet.md) |
| Core services | `AddCoreServices()` | `CrestApps.Core` | [Core Services](./core-services.md) |
| AI services | `AddCoreAIServices()` | `CrestApps.Core.AI` | [AI Core](./ai-core.md) |
| Orchestration | `AddCoreAIOrchestration()` | `CrestApps.Core.AI` | [Orchestration Overview](../orchestration/index.md) |
| Chat | `AddCoreAIChatInteractions()` | `CrestApps.Core.AI.Chat` | [Chat Interactions](./chat.md) |
| Documents | `AddCoreAIDocumentProcessing()` | `CrestApps.Core.AI.Documents` | [Document Processing](./document-processing.md) |
| Templates | `AddTemplating()` | `CrestApps.Core.Templates` | [AI Templates](./ai-templates.md) |
| Custom tools | `AddCoreAITool<T>()` | `CrestApps.Core.AI` | [Custom AI Tools](./tools.md) |
| Agents | Agent and orchestration registrations | `CrestApps.Core.AI` | [AI Agents](./agents.md) |
| Copilot orchestration | `AddCoreAICopilotOrchestrator()` | `CrestApps.Core.AI.Copilot` | [Copilot Orchestrator](../orchestration/copilot.md) |
| Claude orchestration | `AddCoreAIClaudeOrchestrator()` | `CrestApps.Core.AI.Claude` | [Claude Orchestrator](../orchestration/claude.md) |
| SignalR and widgets | `AddCoreSignalR()` | `CrestApps.Core.SignalR` | [SignalR](./signalr.md) |
| Data storage | Store registration extensions | `CrestApps.Core.Data.YesSql` | [Data Storage](./data-storage.md) |
| AI clients | Provider-specific extensions | Provider packages | [AI Clients](../providers/index.md) |
| Data sources | Backend-specific extensions | Search packages | [Data Sources](../data-sources/index.md) |
| MCP | `AddCoreAIMcpClient()` / `AddCoreAIMcpServer()` | `CrestApps.Core.AI.Mcp` | [MCP](../mcp/index.md) |
| A2A | `AddCoreAIA2AClient()` | `CrestApps.Core.AI.A2A` | [A2A](../a2a/index.md) |

## Start with outcomes

If you want to evaluate the framework by business need instead of package names, start with **[AI Chat Use Cases](./use-cases.md)**.
