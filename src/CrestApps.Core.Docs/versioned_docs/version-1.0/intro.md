---
sidebar_label: Introduction
sidebar_position: 1
title: CrestApps.Core
description: The standalone CrestApps framework for building AI-powered ASP.NET Core applications.
---

# CrestApps.Core

**CrestApps.Core is a composable AI management and application framework for .NET.** It is designed for teams that want to add advanced AI capabilities to an existing application without spending months rebuilding orchestration, chat, retrieval, reporting, and integration plumbing from scratch.

## What it delivers

- provider-agnostic AI management with connections, deployments, agent profiles, and data sources
- reusable AI agent profiles that define behavior, prompts, models, tools, and defaults
- chat interactions for playground and production scenarios
- document upload and processing for Q&A, summarization, extraction, and tabulation
- RAG across search indexes, attached files, and user memory
- AI agents, MCP, A2A, and remote host integration
- custom AI functions and deep runtime extensibility
- chat metrics, reporting, lead workflows, post-session processing, and live-agent handoff

## Why teams choose it

| Need | What CrestApps.Core gives you |
| --- | --- |
| A single framework instead of scattered SDK examples | One composable service model for AI integration in .NET |
| Reusable behavior across sessions | AI agent profiles, templates, orchestration, and shared defaults |
| Production chat flows | Sessions, widgets, metrics, response handlers, extraction, and escalation workflows |
| Knowledge-grounded AI | Documents, data sources, vector search, citations, and configurable preemptive RAG |
| Protocol interoperability | MCP server/client support and A2A-ready agent workflows |
| Business-ready extensibility | Custom AI tools, custom handlers, custom stores, and application-specific rules |

## Application models

The framework fits standard .NET dependency injection and works well in:

- **MVC** and **Razor Pages** applications
- **Blazor Server** and **Blazor Web App** projects
- **Minimal API** backends
- **.NET MAUI Hybrid / Blazor Hybrid** applications

## Start here

- **[Getting Started](getting-started.md)** for the quickest path from package install to first prompt
- **[Core Overview](core/index.md)** for the feature catalog and package layout
- **[AI Chat Use Cases](core/use-cases.md)** for real-world scenarios
- **[MVC Example](core/mvc-example.md)** for the complete reference host
