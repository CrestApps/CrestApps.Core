---
sidebar_label: Glossary
sidebar_position: 99
title: Glossary
description: Definitions for key CrestApps.Core terms and concepts.
---

# Glossary

Quick reference for terminology used throughout CrestApps.Core documentation.

## Connection

An `AIProviderConnection` holds the credentials and endpoint information for a specific AI provider (e.g., OpenAI API key, Azure OpenAI endpoint). Connections are shared across deployments and configured via `CrestApps:AI:Connections`.

## Deployment

An `AIDeployment` maps a logical name to a model within a connection. Each deployment specifies a model name, deployment type (Chat, Embedding, Utility, etc.), and an optional connection reference. Configured via `CrestApps:AI:Deployments`.

## Profile

An `AIProfile` is a high-level configuration that groups a chat deployment, system prompt, tools, and behavioral settings into a named entity. Profiles define _what_ the AI assistant does and _how_ it behaves. Use profiles when building user-facing AI experiences.

## Chat Interaction

A `ChatInteraction` extends a profile with UI-specific settings such as SignalR hub routes, chat widget theming, response handler names, and session management policies. Chat Interactions power the built-in playground-style chat experience.

## Chat Session

An `AIChatSession` tracks a single conversation between a user and an AI assistant. It stores the session ID, profile reference, user identity, status, and message history (via prompts). Sessions are persisted through an `IAIChatSessionManager`.

## Orchestrator

An `IOrchestrator` manages the complete lifecycle of a multi-turn AI conversation. It handles tool calling, message enrichment, streaming, and the iterative loop between the model and tools. CrestApps ships with a `DefaultOrchestrator`, plus specialized orchestrators for Claude and Copilot.

## Catalog

A generic CRUD abstraction (`ICatalog<T>`) that provides `FindByIdAsync`, `GetAllAsync`, `CreateAsync`, `UpdateAsync`, and `DeleteAsync` for any entity type. Catalogs are the persistence layer interface used across the framework.

## Store

A concrete implementation of a catalog backed by a specific data provider. Examples: `EntityCoreAIChatSessionManager` (EF Core + SQLite), `YesSqlAIChatSessionManager` (YesSql). Stores are swappable through DI.

## Context Builder

An `IAICompletionContextBuilder` (or `IOrchestrationContextBuilder`) runs a pipeline of handlers that enrich a context object before it reaches the AI provider. Use context builders to inject system prompts, attach tools, apply settings, or transform the request.

## Tool

An `AITool` is a function the AI model can call during a conversation. Tools inherit from `AIFunction` (from `Microsoft.Extensions.AI`) and are registered via `AddCoreAITool<T>(name)`. The orchestrator automatically selects and invokes tools based on model requests.

## Tool Registry

An `IToolRegistry` provides the set of tools available for a given orchestration context. Tool registries can filter tools by purpose, relevance scoring, or manual selection. Providers like MCP and A2A contribute tools through `IToolRegistryProvider`.

## Provider

An `IAIClientProvider` creates the SDK client objects (`IChatClient`, `IEmbeddingGenerator`) for a specific AI backend. Each provider (OpenAI, Azure OpenAI, Ollama, etc.) registers itself and is selected based on the connection's `ClientName`.

## MCP (Model Context Protocol)

A protocol for connecting AI models to external tools and resources. CrestApps supports MCP as both a client (consuming tools from MCP servers) and a server (exposing prompts and resources to MCP clients).

## A2A (Agent-to-Agent Protocol)

A protocol for AI agents to discover and communicate with each other. CrestApps supports A2A as both a client (discovering and invoking remote agents) and a host (exposing local agents to remote callers).

## Extensible Entity

The `ExtensibleEntity` base class provides a `Properties` dictionary for schema-flexible persistence. Any entity can store additional typed data without modifying the database schema, using `Put<T>()` and `TryGet<T>()`.

## Data Source

An `AIDataSource` configures a searchable knowledge base backed by Azure AI Search or Elasticsearch. Data sources power RAG (Retrieval-Augmented Generation) workflows where the model queries indexed documents during a conversation.

## Search Index Profile

A `SearchIndexProfile` defines a named search index with its backing provider, field mappings, and indexing configuration. Used by document processing and data source features to manage index lifecycle.
