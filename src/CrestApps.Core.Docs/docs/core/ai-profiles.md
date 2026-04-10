---
sidebar_label: AI Profiles
sidebar_position: 4
title: AI Profiles
description: Understand AI Profiles as the reusable runtime contract that powers chat, agents, orchestration, memory, and retrieval across CrestApps.Core.
---

# AI Profiles

> The reusable contract that tells CrestApps.Core **how an AI experience should behave**, not just which model to call.

An **AI Profile** is the main composition unit for higher-level AI features in `CrestApps.Core`. It groups the instructions, deployments, orchestrator choice, tools, knowledge, and session-processing rules that define a reusable AI experience.

If a deployment answers **"which model should run?"**, an AI Profile answers **"how should this experience behave from start to finish?"**

## Why AI Profiles matter

Profiles are used across many parts of the framework because they let you define AI behavior once and reuse it consistently:

- **AI Chat** uses a profile as the session contract for reusable conversations
- **Agents** use profiles to describe specialized behavior and routing intent
- **Orchestration** reads the profile to decide how prompts, tools, and downstream steps should run
- **Knowledge-aware chat** uses profile-attached documents and data sources for retrieval
- **Memory and analytics** use profile settings to control long-lived personalization and post-session processing
- **Templates** can prefill or stamp profile behavior so teams do not repeat the same configuration manually

## AI Profile vs. other AI building blocks

| Concept | Purpose | Best way to think about it |
| --- | --- | --- |
| **AI Connection** | Stores provider credentials and endpoint details | "How do I talk to a provider?" |
| **AI Deployment** | Maps a logical deployment name to a concrete model on a provider/connection | "Which model should be used?" |
| **Chat Interactions** | Playground-style or ad hoc conversations with directly chosen parameters | "Let me test this setup quickly." |
| **AI Profile** | Reusable runtime behavior for chat, agents, orchestration, knowledge, and processing | "How should this AI experience behave?" |
| **AI Chat** | Session-driven chat experience built around a selected profile | "Run ongoing conversations from this reusable profile." |

## When to use Chat Interactions vs. AI Profiles

Start with **Chat Interactions** when you want the fastest validation path for a new provider connection and deployment.

Move to **AI Profiles** when you want any of the following:

- a reusable system prompt or welcome experience
- a stable deployment choice for repeated sessions
- orchestration and tool usage
- knowledge retrieval from documents or data sources
- memory, analytics, extraction, or post-session behavior
- agent-style routing or specialized assistant identities

## What an AI Profile contains

The exact fields depend on enabled features, but a profile can act as the home for:

### 1. Identity and purpose

- technical name
- display title
- profile type
- description, especially for agent profiles

This gives the runtime and UI a stable identity for the experience.

### 2. Deployment selection

A profile can point to:

- a **chat deployment** for primary conversational responses
- a **utility deployment** for supporting tasks such as planning, extraction, or summarization

That lets the same profile use different models for different responsibilities.

### 3. Prompt and conversation behavior

Profiles can define:

- system instructions
- welcome message
- initial assistant prompt
- prompt subject
- prompt templates
- completion settings such as temperature, top-p, penalties, token limits, and past-message depth

This is where you shape tone, constraints, and conversation style.

### 4. Orchestration and tool usage

Profiles can select:

- an orchestrator
- local tools
- agent references
- remote A2A connections
- remote MCP connections

This is why profiles are broader than plain chat presets. They can define how the AI experience coordinates work, not just how it talks.

### 5. Knowledge and retrieval

Profiles can be linked to:

- uploaded profile documents
- session document behavior
- index-backed data sources
- retrieval tuning such as strictness, top-N, scope, and filters

This makes the profile the reusable knowledge boundary for RAG-oriented experiences.

### 6. Session and outcome processing

Profiles can enable:

- extracted data definitions
- session metrics
- AI resolution detection
- conversion goals
- post-session processing tasks

That turns a profile into more than a prompt container. It becomes the contract for what should happen during and after a session.

### 7. Memory and personalization

Profiles can opt into user memory so experiences can carry durable context forward between sessions instead of starting from zero every time.

## Profile types

`AIProfile.Type` lets one model support different runtime roles.

Common examples:

- **Chat** for reusable conversational assistants
- **Agent** for specialized routed behavior that an orchestrator can call when appropriate
- **TemplatePrompt** when the profile is oriented around prompt generation or reusable prompt-driven tasks

The important idea is that the profile type changes how the framework interprets and uses the same underlying profile record.

## Typical lifecycle

1. Create a provider connection.
2. Create one or more deployments.
3. Use **Chat Interactions** to verify the model behaves correctly.
4. Create an AI Profile once you want a reusable behavior contract.
5. Attach tools, documents, data sources, memory, or post-session rules as needed.
6. Use the profile from AI Chat, agents, orchestrators, or other runtime features.

## Practical examples

### Example 1: Reusable support assistant

Use an AI Profile when you want:

- a fixed support tone
- a shared knowledge base
- extracted contact or issue fields
- post-session resolution analysis

This profile can then power every support chat session consistently.

### Example 2: Specialized agent

Use an AI Profile when you want:

- a description that explains what the agent is good at
- a specific deployment and tool set
- orchestration-based routing into that agent

The profile becomes the unit the orchestrator can reason about and invoke.

### Example 3: Knowledge-aware internal assistant

Use an AI Profile when you want:

- indexed data sources
- attached profile documents
- stricter retrieval settings
- user memory for returning employees

That profile can then serve as a reusable internal assistant instead of rebuilding the configuration per session.

## Design guidance

- Use **deployments** to separate model selection from behavior.
- Use **profiles** to capture reusable behavior and lifecycle rules.
- Use **Chat Interactions** for fast testing and experimentation.
- Use **AI Chat** when you want repeatable session-based experiences built on top of a profile.

## Related docs

- [AI Core](./ai-core.md)
- [Chat Interactions](./chat.md)
- [AI Templates](./ai-templates.md)
- [AI Agents](./agents.md)
- [MVC Example](./mvc-example.md)
