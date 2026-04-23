---
sidebar_label: Overview
sidebar_position: 1
title: Agent-to-Agent Protocol (A2A)
description: Connect to remote AI agents and expose your own agents using the A2A protocol for cross-application agent collaboration.
---

# Agent-to-Agent Protocol (A2A)

> Discover, invoke, and expose AI agents across application boundaries using the [Agent-to-Agent (A2A) protocol](https://google.github.io/A2A/).

## What Is A2A?

The Agent-to-Agent (A2A) protocol, developed by Google, is an open standard that enables AI agents running in **different applications** to discover each other, negotiate capabilities, and delegate tasks — all over HTTP. Unlike tool-calling protocols that expose individual functions, A2A operates at the **agent level**: a remote agent is a self-contained entity with its own reasoning, tools, and context.

Key concepts:

| Concept | Description |
|---------|-------------|
| **Agent Card** | A JSON document published by a host that describes the agent's name, description, skills, and endpoint URL. Clients fetch this to discover what a host offers. |
| **Skill** | A named capability advertised on an Agent Card (e.g., "translate-text", "summarize-document"). Each skill becomes an invokable tool on the client side. |
| **Host** | An application that **exposes** one or more AI agents to remote clients. |
| **Client** | An application that **discovers and invokes** remote agents hosted elsewhere. |
| **Message** | The unit of communication — a client sends an `AgentMessage` to the host and receives a response containing text, artifacts, or task status. |

## When to Use A2A vs MCP

Both protocols connect AI systems across boundaries, but they solve different problems:

| Criteria | A2A | MCP |
|----------|-----|-----|
| **Abstraction level** | Agent-level (send a task, get a result) | Tool-level (call a function, get a return value) |
| **Best for** | Delegating complex, multi-step work to a remote AI agent | Exposing individual functions, data sources, or resources |
| **Remote agent has its own AI model?** | ✅ Yes — the remote agent reasons independently | ❌ No — tools are stateless functions |
| **Conversation context** | Maintained via `contextId` across messages | Stateless per tool call |
| **Discovery** | Agent Cards with skills | Tool lists with JSON schemas |
| **Use when** | "Ask the legal team's agent to review this contract" | "Call the weather API to get today's forecast" |

**Rule of thumb**: If the remote system needs to **think** (use an AI model, maintain context, choose its own tools), use A2A. If it just needs to **do** (execute a function and return data), use MCP.

You can use both in the same application — A2A for agent delegation and MCP for tool access.

## Architecture

```text
┌─────────────────────────────────┐          ┌──────────────────────────────────┐
│         A2A CLIENT              │          │          A2A HOST                │
│                                 │          │                                  │
│  ┌───────────┐                  │   HTTP   │                ┌──────────────┐  │
│  │ AI Model  │                  │ ◄──────► │                │  AI Profiles │  │
│  └─────┬─────┘                  │          │                └──────┬───────┘  │
│        │ tool call              │          │                       │          │
│  ┌─────▼──────────────────┐     │          │  ┌───────────────────▼────────┐  │
│  │ A2AToolRegistryProvider│     │          │  │   Agent Card Generator     │  │
│  │  (discovers skills as  │     │          │  │  (profiles → agent cards)  │  │
│  │   tool entries)        │     │          │  └───────────────────┬────────┘  │
│  └─────┬──────────────────┘     │          │                     │            │
│        │                        │          │  ┌──────────────────▼─────────┐  │
│  ┌─────▼──────────────────┐     │  fetch   │  │ /.well-known/agent.json   │   │
│  │ A2AAgentProxyTool      ├─────┼──────────┼──► (Agent Card endpoint)     │   │
│  │  (proxies messages     │     │          │  └───────────────────────────┘   │
│  │   to remote agent)     │     │  send    │                                  │
│  │                        ├─────┼──────────┼──► /a2a (message endpoint)       │
│  └────────────────────────┘     │          │                                  │
│                                 │          │  Authentication:                 │
│  Authentication:                │          │   • OpenID Connect               │
│   • API Key, Basic, OAuth2,     │          │   • API Key                      │
│     mTLS, Custom Headers        │          │   • None (dev only)              │
└─────────────────────────────────┘          └──────────────────────────────────┘
```

## Quick Start

### As a Client (invoke remote agents)

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddA2AClient()
    )
);
```

→ See the [A2A Client](./client) page for connection setup, authentication, and tool registry details.

### As a Host (expose your agents)

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddA2AHost()
    )
);

builder.Services.Configure<A2AHostOptions>(options =>
{
    options.AuthenticationType = A2AHostAuthenticationType.ApiKey;
    options.ApiKey = "your-secret-key";
});
```

→ See the [A2A Host](./host) page for authentication modes, agent card generation, and endpoint configuration.

## Sub-Pages

| Page | Description |
|------|-------------|
| [A2A Client](./client) | Discover and invoke remote A2A agents — connection management, tool registry, authentication, built-in discovery tools |
| [A2A Host](./host) | Expose your AI agents to remote clients — host configuration, authentication modes, agent card generation |

## Repository examples

Use these projects when you want to inspect A2A behavior in this repository:

- `src\Startup\CrestApps.Core.Mvc.Web` for the full host composition that includes A2A
- `src\Startup\CrestApps.Core.Mvc.Samples.A2AClient` for a smaller protocol-focused sample client
- `src\Startup\CrestApps.Core.Aspire.AppHost` when you want the composed local environment
