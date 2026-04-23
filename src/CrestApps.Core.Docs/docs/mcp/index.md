---
sidebar_label: Overview
sidebar_position: 1
title: Model Context Protocol (MCP)
description: MCP client and server support for connecting to remote tool servers and exposing your application's tools.
---

# Model Context Protocol (MCP)

> Use MCP when you need tool-, prompt-, or resource-level interoperability. CrestApps.Core supports both sides of the protocol: consuming remote MCP servers and exposing your own application as one.

The [Model Context Protocol](https://modelcontextprotocol.io/) standardizes how AI applications discover and invoke tools, prompts, and resources across process boundaries. The framework provides both sides of the protocol.

## Client — Consume Remote MCP Servers

Connect to external MCP servers, discover their tools, and make them available in the AI orchestrator.

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddMcpClient()
    )
);
```

**Key capabilities:**

- **SSE transport** — Connect to remote MCP servers over HTTP with full authentication support (API key, Basic, OAuth2, custom headers)
- **StdIO transport** — Communicate with locally installed MCP server processes via stdin/stdout
- **Automatic tool discovery** — Discovered tools appear in the orchestrator's tool registry and are invoked transparently
- **Capability resolution** — Semantic similarity filtering to select relevant tools from large MCP server catalogs

📖 **[MCP Client →](./client.md)** — Transport configuration, authentication, discovery, and tool-registry integration details.

## Server — Expose Your AI Capabilities

Expose your registered AI tools, prompts, and resources to external MCP clients.

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddMcpServer()
    )
);
```

Or, using the builder pattern with stores:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddMcpServer(mcpServer => mcpServer
            .AddYesSqlStores()
            .AddFtpResources()
        )
    )
);
```

**Key capabilities:**

- **Tool exposure** — Registered AI tools become callable by external MCP clients
- **Prompt serving** — Prompts from the catalog and code-registered instances are listed and retrievable
- **Resource serving** — Files and data served through pluggable resource type handlers (FTP, SFTP, custom)
- **Authentication** — OpenID Connect, API key, or no-auth for development

📖 **[MCP Server →](./server.md)** — Full documentation with endpoint setup, authentication, and resource configuration.

## Resource Type Handlers

Create custom resource type handlers to serve files, data, or content from any protocol as MCP resources.

```csharp
builder.Services
    .AddCoreAIMcpServer()
    .AddMcpResourceType<MyDatabaseResourceHandler>("database");
```

📖 **[Resource Types →](./resource-types.md)** — Implementation guide with built-in handlers and custom handler examples.

