---
sidebar_label: MCP Server
sidebar_position: 3
title: MCP Server
description: Expose your application's tools, prompts, and resources as an MCP server for external AI clients.
---

# MCP Server

> Expose your registered AI tools, prompts, and resources to external MCP clients over HTTP.

## Quick Start

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIOrchestration()
    .AddCoreAIMcpServer()
    .AddCoreAIFtpMcpResources()
    .AddCoreAISftpMcpResources();
```

Or, using the builder pattern:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddMcpServer(mcpServer => mcpServer
            .AddYesSqlStores()
            .AddFtpResources()
            .AddSftpResources()
        )
    )
    .AddYesSqlDataStore(configuration => configuration
        .UseSqLite("Data Source=app.db;Cache=Shared")
    )
);
```

`AddCoreAIMcpServer()` registers the shared prompt and resource services. FTP and SFTP resource handlers now live in the optional `CrestApps.Core.AI.Mcp.Ftp` and `CrestApps.Core.AI.Mcp.Sftp` packages, so hosts opt into those transport dependencies explicitly.

### Registering MCP Server Stores

The MCP server requires stores for `McpPrompt` and `McpResource` catalogs. Register them on the MCP server builder:

**Entity Framework Core (via builder):**

```csharp
.AddMcpServer(mcpServer => mcpServer
    .AddEntityCoreStores()
)
```

**YesSql (via builder):**

```csharp
.AddMcpServer(mcpServer => mcpServer
    .AddYesSqlStores()
)
```

Or use the `IServiceCollection` extensions directly: `AddCoreAIMcpServerStoresYesSql()` / `AddCoreAIMcpServerStoresEntityCore()`.

When the same host also acts as an MCP client, use `AddCoreAIMcpServices()` or `AddCoreAIMcpClient(...)` once for the shared runtime pieces, then layer `AddCoreAIMcpServer()` on top for the prompt and resource services. The MCP client stores (`McpConnection` catalog) and MCP server stores (`McpPrompt`, `McpResource` catalogs) are registered independently.

## Problem & Solution

External AI clients — IDE assistants, chat agents, orchestration frameworks — need a standardized way to discover and call your application's tools, read your prompts, and access your resources. The [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) provides that standard.

The MCP server framework:

- Exposes your registered AI tools so external clients can invoke them
- Serves prompts from the catalog and from code-registered `McpServerPrompt` instances
- Serves resources (files, data, templates) via pluggable resource type handlers
- Supports OpenID Connect, API key, or no-auth for development
- Uses the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) for HTTP transport

## Registered Services

`AddCoreAIMcpServer()` registers these services:

| Service | Implementation | Lifetime | Purpose |
|---------|---------------|----------|---------|
| `IMcpServerPromptService` | `DefaultMcpServerPromptService` | Scoped | Lists and retrieves server prompts |
| `IMcpServerResourceService` | `DefaultMcpServerResourceService` | Scoped | Lists, templates, and reads server resources |

### Optional transport resource packages

| Extension | Package | Purpose |
|-----------|---------|---------|
| `AddCoreAIFtpMcpResources()` | `CrestApps.Core.AI.Mcp.Ftp` | Registers the FTP/FTPS MCP resource type handler |
| `AddCoreAISftpMcpResources()` | `CrestApps.Core.AI.Mcp.Sftp` | Registers the SFTP MCP resource type handler |

## Server Endpoint Setup

Use the [ModelContextProtocol SDK](https://github.com/modelcontextprotocol/csharp-sdk) to map the SSE endpoint and wire in your tool, prompt, and resource handlers:

```csharp
app.MapMcpSse(mcpBuilder =>
{
    mcpBuilder.WithHttpTransport(options =>
    {
        options.ServerInfo = new() { Name = "My MCP Server", Version = "1.0" };
    });

    mcpBuilder.WithListToolsHandler(async (context, ct) =>
    {
        // Return your registered AI tools
    });

    mcpBuilder.WithCallToolHandler(async (context, ct) =>
    {
        // Invoke a tool by name, delegate to your tool registry
    });

    mcpBuilder.WithListPromptsHandler(async (context, ct) =>
    {
        var service = context.Services.GetRequiredService<IMcpServerPromptService>();
        return new ListPromptsResult { Prompts = await service.ListAsync() };
    });

    mcpBuilder.WithGetPromptHandler(async (context, ct) =>
    {
        var service = context.Services.GetRequiredService<IMcpServerPromptService>();
        return await service.GetAsync(context, ct);
    });

    mcpBuilder.WithListResourcesHandler(async (context, ct) =>
    {
        var service = context.Services.GetRequiredService<IMcpServerResourceService>();
        return new ListResourcesResult { Resources = await service.ListAsync() };
    });

    mcpBuilder.WithListResourceTemplatesHandler(async (context, ct) =>
    {
        var service = context.Services.GetRequiredService<IMcpServerResourceService>();
        return new ListResourceTemplatesResult
        {
            ResourceTemplates = await service.ListTemplatesAsync()
        };
    });

    mcpBuilder.WithReadResourceHandler(async (context, ct) =>
    {
        var service = context.Services.GetRequiredService<IMcpServerResourceService>();
        return await service.ReadAsync(context, ct);
    });
});
```

See the [MVC Example](../core/mvc-example.md) for a complete working server setup.

## Prompt Serving

### IMcpServerPromptService

Serves prompts from two sources:

1. **Catalog prompts** — `McpPrompt` entries stored in the named catalog (`INamedCatalog<McpPrompt>`)
2. **SDK prompts** — `McpServerPrompt` instances registered directly in DI

```csharp
public interface IMcpServerPromptService
{
    Task<IList<Prompt>> ListAsync();
    Task<GetPromptResult> GetAsync(
        RequestContext<GetPromptRequestParams> request,
        CancellationToken cancellationToken = default);
}
```

The `DefaultMcpServerPromptService` merges both sources, with catalog prompts taking precedence when names collide.

### McpPrompt Model

```csharp
public sealed class McpPrompt : CatalogItem, INameAwareModel
{
    public string Name { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string Author { get; set; }
    public string OwnerId { get; set; }
    public Prompt Prompt { get; set; }  // MCP SDK Prompt with arguments
}
```

## Resource Serving

### IMcpServerResourceService

Serves resources and resource templates from the catalog and code-registered `McpServerResource` instances:

```csharp
public interface IMcpServerResourceService
{
    Task<IList<Resource>> ListAsync();
    Task<IList<ResourceTemplate>> ListTemplatesAsync();
    Task<ReadResourceResult> ReadAsync(
        RequestContext<ReadResourceRequestParams> request,
        CancellationToken cancellationToken = default);
}
```

**How resource reading works:**

1. The service checks if any registered `McpServerResource` matches the URI
2. If not, it parses the URI to extract the resource `ItemId` (from the path segment after `://`)
3. Looks up the `McpResource` catalog entry and resolves the `IMcpResourceTypeHandler` by the entry's `Source` (type key)
4. For template URIs, extracts variables using `McpResourceUri.TryMatch()` and passes them to the handler

### McpResource Model

```csharp
public sealed class McpResource : SourceCatalogEntry, IDisplayTextAwareModel
{
    public string DisplayText { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string Author { get; set; }
    public string OwnerId { get; set; }
    public Resource Resource { get; set; }  // MCP SDK Resource (URI, name, description, MIME type)
}
```

### URI Templates

Resources can use URI templates with `{variable}` placeholders. The `McpResourceUri` utility handles matching and variable extraction:

```
ftp://my-resource/{path}
recipe-step-schema://my-resource/{stepName}
```

When a client requests `ftp://my-resource/reports/2024/sales.csv`, the framework matches it against the template and extracts `{ "path": "reports/2024/sales.csv" }`. The last variable in a template can match multi-segment paths.

## Resource Type Handlers

Optional transport resource handlers:

| Type | Handler | Description |
|------|---------|-------------|
| `ftp` | `FtpResourceTypeHandler` | Registered by `AddCoreAIFtpMcpResources()` from `CrestApps.Core.AI.Mcp.Ftp` |
| `sftp` | `SftpResourceTypeHandler` | Registered by `AddCoreAISftpMcpResources()` from `CrestApps.Core.AI.Mcp.Sftp` |

### Registering Custom Resource Types

```csharp
builder.Services.AddMcpResourceType<BlobStorageResourceHandler>("blob", entry =>
{
    entry.DisplayName = new LocalizedString("Blob Storage", "Azure Blob Storage");
    entry.Description = new LocalizedString("Blob Desc", "Reads content from Azure Blob Storage.");
    entry.SupportedVariables =
    [
        new McpResourceVariable("path")
        {
            Description = new LocalizedString("Blob Path", "The blob path within the container.")
        },
    ];
});
```

For a full guide on implementing custom resource type handlers, see [Resource Types](./resource-types.md).

## Authentication

### McpServerOptions

Configure server authentication via `McpServerOptions`:

```csharp
services.Configure<McpServerOptions>(options =>
{
    options.AuthenticationType = McpServerAuthenticationType.OpenId;
    options.RequireAccessPermission = true;
});
```

### McpServerAuthenticationType

| Type | Description |
|------|-------------|
| `OpenId` | **(Default)** Uses OpenID Connect authentication via the `"Api"` scheme. Most secure option for production. |
| `ApiKey` | Uses a predefined API key provided via the `Authorization` header. Configure the key in `McpServerOptions.ApiKey`. |
| `None` | Disables authentication. **Only for local development.** |

### Permission Control

When using `OpenId` authentication, the `RequireAccessPermission` property (default: `true`) controls whether the `AccessMcpServer` permission is checked. When set to `false`, any authenticated user can access the MCP server.

**Example** — API key authentication:

```csharp
services.Configure<McpServerOptions>(options =>
{
    options.AuthenticationType = McpServerAuthenticationType.ApiKey;
    options.ApiKey = builder.Configuration["Mcp:ServerApiKey"];
});
```

## Tool Exposure

When your application acts as an MCP server, tool exposure is **opt-in**. Nothing is listed or callable by default: the server only exposes the tools and configured [tool instances](../core/tool-instances.md) that you explicitly allow through the `McpServerOptions` site settings.

```csharp
services.Configure<McpServerOptions>(options =>
{
    // Expose specific tools and tool instances by name.
    options.Tools = ["crestapps-docs", "weather"];

    // Or expose every tool and tool instance.
    // When set to true, the Tools allow-list above is ignored.
    options.ExposeAllTools = true;
});
```

| Property | Effect |
|----------|--------|
| `Tools` | An allow-list of tool and tool instance names to expose. Matching is case-insensitive. |
| `ExposeAllTools` | When `true`, every **selectable** tool and tool instance is exposed and the allow-list is ignored. |

Only selectable tools are ever exposed over MCP. System tools (those marked `IsSystemTool`, which agents auto-include based on context) and hidden tools are never listed or callable — not even when `ExposeAllTools` is `true`.

Because `McpServerOptions` is backed by site settings, an operator can choose which tools to expose from the admin **Settings → MCP server** page without redeploying. The allow-list is enforced by **both** the list and call handlers, so a tool that is not exposed can neither be discovered nor invoked.

Both code-registered tools (from `AddCoreAITool<T>()`, see [Custom Tools](../core/tools.md)) and stored tool instances (from any registered [tool instance source](../core/tool-instances.md), such as the [documentation search sources](#exposing-a-documentation-knowledge-base)) participate in the same allow-list.

## Exposing Agents as Tools

An **agent** — an AI profile of type agent — can be exposed to MCP clients as a callable tool, alongside plain tools and tool instances. The client sees one tool per allow-listed agent, named after the agent and described by its description, taking a single `prompt` argument. Invoking it runs that agent.

```csharp
services.Configure<McpServerOptions>(options =>
{
    // Expose specific agents by name.
    options.Agents = ["researcher", "report-writer"];

    // Or expose every agent that has a description.
    // When true, the Agents allow-list above is ignored.
    options.ExposeAllAgents = true;
});
```

| Property | Effect |
|----------|--------|
| `Agents` | An allow-list of agent profile names to expose. Matching is case-insensitive. |
| `ExposeAllAgents` | When `true`, every described agent is exposed and the allow-list is ignored. |

:::warning
Agents are gated by their **own** switch, separate from `ExposeAllTools`. This is deliberate: an agent runs a whole profile — its system message, its tools, its data sources, and the credentials behind them — so a server that had already opted into exposing tools must not start exposing agents merely because it upgraded. The allow-list is a security boundary; an exposed agent is runnable by any client that clears the server's authentication.
:::

### An agent's own tools are not filtered by the allow-list

The two lists answer different questions, and they never intersect:

| | Question it answers |
|---|---|
| `Tools` / `ExposeAllTools` | What may a client **directly name and invoke**? |
| The agent's own profile | What does the agent use **internally** to do its job? |

An allow-listed agent runs with the tools, tool instances, data sources, and MCP client connections its profile configures — whether or not any of those appear in `Tools`. Nor does using a tool internally make it directly callable: it stays unreachable by name unless separately allow-listed.

Intersecting the two would be backwards. An operator wanting a working agent would have to allow-list every tool it uses internally, and each of those would then become directly callable — forcing *more* exposure, and defeating the point of publishing a curated agent instead of raw tools. Think of it as encapsulation: exposing an agent exposes a function signature, not its implementation.

### What an agent may do when reached over MCP

- **Its own tools run only if the agent opted in.** `AgentMetadata.AllowToolInvocation` must be `true`, exactly as when the model invokes that agent as a tool in a chat completion. It defaults to `false`, so an agent that answers suspiciously generically over MCP is usually missing this flag rather than hitting a server problem.
- **Nested agents stay suppressed.** An agent reached over MCP can use its tools but cannot delegate to other agents; that bound is what makes running with tools safe.
- **Availability is irrelevant.** `AgentAvailability.AlwaysAvailable` versus `OnDemand` governs a completion's token budget, not who may reach an agent from outside. The allow-list is the only gate.
- **A name shared with a tool resolves to the tool.** Agents are enumerated last, and one whose name is already taken is skipped from the listing with a warning, so what is listed is always what a call will reach.

The call handler establishes an AI invocation scope for the duration of every MCP tool call, which is what places an agent at top-level depth and lets it run its configured tools. Without it an agent would silently fall back to a tool-less completion — a worse answer rather than an error.

Because `McpServerOptions` is backed by site settings, agents are chosen from the admin **Settings → MCP server** page without redeploying.

## Exposing a documentation knowledge base

A common reason to run an MCP server is to answer questions from product or framework documentation that lives on a public site (such as a Docusaurus or MkDocs site). Instead of indexing that content into a vector store, the built-in documentation search [tool instance sources](../core/tool-instances.md) let an operator declare a documentation site as a **tool instance** and scan it on demand.

These sources are ordinary tool instance sources registered on the tool instances builder, so they are usable with or without the MCP server. Each configured instance binds one site and surfaces as its own callable function, so a host can offer "search the CrestApps docs" and "search the Orchard Core docs" as two distinct tools. Instances are persisted and managed through the standard tool instance store and UI, and are exposed to MCP clients through the [allow-list](#tool-exposure) above — nothing is exposed until you opt in.

### Registering the sources

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddToolInstances(toolInstances => toolInstances
            .AddDocumentationSearchSources()
            .AddYesSqlStores()
        )
        .AddMcpServer(mcpServer => mcpServer
            .AddYesSqlStores()
        )
    )
    .AddYesSqlDataStore(configuration => configuration
        .UseSqLite("Data Source=app.db;Cache=Shared")
    )
);
```

`AddDocumentationSearchSources()` registers all three built-in sources. To register only the ones you need, call the individual `AddSitemapDocumentationSource()`, `AddSearchIndexDocumentationSource()`, and `AddAlgoliaDocumentationSource()` methods instead. Once the sources are registered, operators create configured instances (each bound to one site) through the tool instances UI or store, and each instance becomes a callable function the AI model can invoke.

### Search strategies

A documentation site can be indexed in different ways depending on what the generator publishes. Each strategy is its own source with its own settings model, so you pick the one that matches the site.

| Source | Registration | Best for | How it works |
|--------|--------------|----------|--------------|
| Sitemap crawl | `AddSitemapDocumentationSource()` | Any site that publishes `sitemap.xml` (Docusaurus, MkDocs, and most static sites). | Crawls pages, strips HTML, and ranks locally with keyword scoring. |
| Search index | `AddSearchIndexDocumentationSource()` | MkDocs Material and other sites that publish a fetchable `search_index.json`. | Downloads the prebuilt index once and ranks its entries locally. |
| Algolia DocSearch | `AddAlgoliaDocumentationSource()` | Docusaurus sites (and others) wired to hosted Algolia DocSearch. | Forwards the query to Algolia, which performs the ranking. |

The registered source names are defined by `DocumentationToolConstants` (`sitemap-documentation`, `search-index-documentation`, `algolia-documentation`), and all three carry the `Knowledgebase` category.

Each strategy binds a settings model:

- **`SitemapDocumentationToolSettings`** — `BaseUrl` (site root, for example `https://core.crestapps.com`), optional `SitemapUrl` (defaults to `{BaseUrl}/sitemap.xml`), optional `MaxResults`, and optional `MaxPages`.
- **`SearchIndexDocumentationToolSettings`** — `BaseUrl` (used to resolve relative locations and the default index URL), optional `IndexUrl` (defaults to `{BaseUrl}/search/search_index.json`), and optional `MaxResults`. This targets the MkDocs Material `search_index.json` schema (`{ "docs": [ { "location", "title", "text" } ] }`).
- **`AlgoliaDocumentationToolSettings`** — `ApplicationId`, `ApiKey` (Algolia **search-only** key, never a write key), `IndexName`, and optional `MaxResults`.

### Example: a public Docusaurus site

A public Docusaurus site that requires no authentication — such as [core.crestapps.com](https://core.crestapps.com) — only needs the sitemap crawl source. Docusaurus publishes a standard `sitemap.xml` at the site root, so the crawler discovers `{BaseUrl}/sitemap.xml` automatically.

1. Register the sitemap source (or all sources) as shown above.
2. Create a tool instance from the **Documentation search (sitemap)** source with a **Name** (for example `crestapps-docs`, the name you expose to MCP clients), a clear **Description**, and a **Base URL** of `https://core.crestapps.com`.
3. Expose the instance through the MCP server by adding its name to the allow-list:

```csharp
services.Configure<McpServerOptions>(options =>
{
    options.Tools = ["crestapps-docs"];
});
```

Because the site is public, no headers, API keys, or credentials are involved — the crawler issues plain anonymous `GET` requests. The first search crawls the site and caches the corpus; later searches reuse the cache.

### Corpus caching

The runtime documentation source (the crawled corpus or downloaded index) is built lazily and cached by a singleton `IDocumentationSourceMaterializer`, keyed by the instance identifier. The cache is rebuilt only when the instance changes, so an edit to a site's settings is picked up on the next search while an unchanged instance reuses its corpus across calls.

### Adding a new documentation source

To support a documentation site that none of the built-in strategies cover, implement a new [tool instance source](../core/tool-instances.md):

1. Create a settings model for the user-provided configuration.
2. Implement `IAIToolInstanceSource.CreateTool(AIToolInstance)` to read the settings and return a `DocumentationSearchToolFunction` (or your own `AIFunction`) bound to a concrete `IDocumentationSource`.
3. Register the source on the tool instances builder with `AddSource<TSource>(name, configure)`.

Operators then create instances of your new source exactly like the built-in ones, and expose them through the same MCP allow-list.

## Server Metadata

### IMcpServerMetadataProvider

Implementations provide server metadata for the MCP handshake:

```csharp
public interface IMcpServerMetadataCacheProvider
{
    Task<McpServerCapabilities> GetCapabilitiesAsync(McpConnection connection);
    Task InvalidateAsync(string connectionId);
}
```

### McpServerMetadata

```csharp
public sealed class McpServerMetadata
{
    public bool UseLocalServer { get; set; }
}
```

## File Provider Integration

### IMcpFileProviderResolver

Allows resource handlers to resolve `IFileProvider` instances by name, enabling resources served from media libraries, web roots, or custom file stores:

```csharp
public interface IMcpFileProviderResolver
{
    IFileProvider Resolve(string providerName);
}
```

## Resource Export

### IMcpResourceHandler

Hook into resource export to strip sensitive data:

```csharp
public interface IMcpResourceHandler
{
    void Exporting(ExportingMcpResourceContext context);
}

public sealed class ExportingMcpResourceContext
{
    public readonly McpResource Resource;
    public readonly JsonObject ExportData;  // Modify to remove credentials
}
```

## Metadata Prompt Generation

### IMcpMetadataPromptGenerator

Generates structured system prompts describing MCP server capabilities so the AI model can reason about when to invoke them:

```csharp
public interface IMcpMetadataPromptGenerator
{
    string Generate(IReadOnlyList<McpServerCapabilities> capabilities);
}
```

The generated prompt is injected into the model context, giving the AI awareness of available MCP capabilities without manually listing each tool in the system prompt.

## Configuration

### McpOptions

`McpOptions` holds the registry of resource types. Resource types are added via `AddMcpResourceType<T>()`:

```csharp
services.Configure<McpOptions>(options =>
{
    // Inspect registered resource types
    foreach (var (type, entry) in options.ResourceTypes)
    {
        Console.WriteLine($"{type}: {entry.DisplayName}");
    }
});
```

Each `McpResourceTypeEntry` provides:

| Property | Description |
|----------|-------------|
| `Type` | Unique type identifier string |
| `DisplayName` | Localized display name |
| `Description` | Localized description |
| `SupportedVariables` | Array of `McpResourceVariable` describing URI template variables |

- Configuring server authentication (OpenID, API key, or none)
- Managing prompts and resources through the admin dashboard
- Registering and configuring resource types
- Viewing server capabilities and health status
