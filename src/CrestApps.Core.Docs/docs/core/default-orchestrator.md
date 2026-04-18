---
sidebar_label: Default Orchestrator
title: Default Orchestrator
description: The built-in CrestApps.Core orchestrator that composes tools, RAG, streaming, and response handling into one execution pipeline.
---

# Default Orchestrator

> The built-in CrestApps.Core orchestration engine that connects the framework's AI clients, tools, retrieval pipelines, response handlers, and streaming loop into one end-to-end execution model.

## What it is

`DefaultOrchestrator` is the framework's first-party `IOrchestrator` implementation. It is the standard orchestrator used when you call `AddCoreAIOrchestration()` and do not select an alternative such as Copilot or Claude.

It is responsible for:

- loading the active AI client and deployment
- building orchestration context from profiles, templates, MCP, data sources, documents, and memory
- scoping tools progressively so large tool catalogs stay usable
- running preemptive RAG before the main completion call
- streaming model output and routing references back to the caller

## When to use it

Use the default orchestrator when you want the full CrestApps.Core pipeline instead of a provider-specific orchestrator runtime.

That is usually the right choice when you need:

- the shared tool and agent pipeline
- preemptive RAG across documents, memory, and data sources
- MCP integration through the framework's own orchestration flow
- predictable host-controlled deployment and connection resolution

## Registration

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIOrchestration()
    .AddCoreAIOpenAI();
```

## Relationship to the orchestration docs

This page is the conceptual overview for the built-in orchestrator.

Use **[Orchestration](./orchestration.md)** for the full pipeline details, registered services, progressive tool scoping, configuration knobs, and extension points.
