---
sidebar_label: Documentation Search
sidebar_position: 5
title: Documentation Search
description: Register tool instance sources that search public documentation sites as a knowledge base.
---

# Documentation Search

> Register documentation search [tool instance](../core/tool-instances.md) sources so operators can configure one callable search function per documentation site (such as Docusaurus or MkDocs) and expose them through an MCP server.

## Problem & Solution

A knowledge-base MCP server often needs to answer questions from product or framework documentation
that lives on public sites. Instead of indexing that content into a vector store, the documentation
search sources let an operator declare a documentation site as a **tool instance** and scan it on
demand.

Rather than a single fixed tool, documentation search ships as [tool instance sources](../core/tool-instances.md):
developer-authored blueprints that a user configures one or more times. Each configured instance binds
one site and surfaces as its own callable function, so a host can offer "search the CrestApps docs" and
"search the Orchard Core docs" as two distinct tools. Instances are persisted and managed through the
standard tool instance store and UI, and are exposed to MCP clients through the server's
[allow-list](./server.md#tool-exposure) — nothing is exposed until you opt in.

## Quick Start

Register the documentation search sources on the tool instances builder:

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

`AddDocumentationSearchSources()` registers all three built-in sources. To register only the ones you
need, call the individual methods instead:

```csharp
.AddToolInstances(toolInstances => toolInstances
    .AddSitemapDocumentationSource()
    .AddSearchIndexDocumentationSource()
    .AddAlgoliaDocumentationSource())
```

Once the sources are registered, operators create configured instances (each bound to one site) through
the tool instances UI or store. Each instance becomes a callable function the AI model can invoke.

## Search Strategies

A documentation site can be indexed in different ways depending on what the generator publishes. Each
strategy is its own source with its own settings model, so you pick the one that matches the site.

| Source | Registration | Best for | How it works |
|--------|--------------|----------|--------------|
| Sitemap crawl | `AddSitemapDocumentationSource()` | Any site that publishes `sitemap.xml` (Docusaurus, MkDocs, and most static sites). | Crawls pages, strips HTML, and ranks locally with keyword scoring. |
| Search index | `AddSearchIndexDocumentationSource()` | MkDocs Material and other sites that publish a fetchable `search_index.json`. | Downloads the prebuilt index once and ranks its entries locally. |
| Algolia DocSearch | `AddAlgoliaDocumentationSource()` | Docusaurus sites (and others) wired to hosted Algolia DocSearch. | Forwards the query to Algolia, which performs the ranking. |

The registered source names are defined by `DocumentationToolConstants`
(`sitemap-documentation`, `search-index-documentation`, `algolia-documentation`), and all three carry
the `Knowledgebase` category.

### Sitemap crawl settings

`SitemapDocumentationToolSettings` binds a sitemap-crawl instance:

| Property | Description |
|----------|-------------|
| `BaseUrl` | Base URL of the documentation site (for example `https://core.crestapps.com`). |
| `SitemapUrl` | Optional explicit sitemap URL. Defaults to `{BaseUrl}/sitemap.xml`. |
| `MaxResults` | Optional maximum results this instance returns per search. |
| `MaxPages` | Optional maximum pages the crawler indexes for this site. |

### Search index settings

`SearchIndexDocumentationToolSettings` binds a search-index instance:

| Property | Description |
|----------|-------------|
| `BaseUrl` | Base URL used to resolve relative entry locations and the default index URL. |
| `IndexUrl` | Optional explicit index URL. Defaults to `{BaseUrl}/search/search_index.json`. |
| `MaxResults` | Optional maximum results this instance returns per search. |

:::note
This targets the MkDocs Material `search_index.json` schema (`{ "docs": [ { "location", "title", "text" } ] }`).
Docusaurus' `@easyops-cn/docusaurus-search-local` plugin stores a client-side Lunr index that is not a
cleanly fetchable JSON document, so use the sitemap crawl or Algolia DocSearch for Docusaurus sites.
:::

### Algolia DocSearch settings

`AlgoliaDocumentationToolSettings` binds an Algolia DocSearch instance:

| Property | Description |
|----------|-------------|
| `ApplicationId` | Algolia application identifier. |
| `ApiKey` | Algolia **search-only** API key (never a write key). |
| `IndexName` | Algolia index name to query. |
| `MaxResults` | Optional maximum results this instance returns per search. |

## Example: a public Docusaurus site

A public Docusaurus site that requires no authentication — such as
[core.crestapps.com](https://core.crestapps.com) — only needs the sitemap crawl source. Docusaurus
publishes a standard `sitemap.xml` at the site root, so the crawler discovers `{BaseUrl}/sitemap.xml`
automatically.

1. Register the sitemap source (or all sources) as shown in [Quick Start](#quick-start).
2. Create a tool instance from the **Documentation search (sitemap)** source with:
   - **Name**: `crestapps-docs` (this is the name you expose to MCP clients)
   - **Description**: a clear sentence such as *"Searches the CrestApps.Core documentation."*
   - **Base URL**: `https://core.crestapps.com`
3. Expose the instance through the MCP server by adding its name to the allow-list:

```csharp
services.Configure<McpServerOptions>(options =>
{
    options.Tools = ["crestapps-docs"];
});
```

Because the site is public, no headers, API keys, or credentials are involved — the crawler issues
plain anonymous `GET` requests through a resilient `HttpClient`. The first search crawls the site and
caches the corpus; later searches reuse the cache.

:::tip
Prefer the sitemap crawl for a public Docusaurus site. Only reach for the Algolia source when the site
is wired to hosted Algolia DocSearch and you have its application ID, search-only API key, and index
name.
:::

## Exposing documentation search through MCP

Documentation search functions are exposed like any other tool instance. Add the instance name to
`McpServerOptions.Tools`, or set `McpServerOptions.ExposeAllTools = true` to expose every non-hidden
tool and instance. Because `McpServerOptions` is backed by site settings, operators can manage the
allow-list from the admin **Settings → MCP server** page. See
[MCP Server → Tool Exposure](./server.md#tool-exposure) for details.

## Corpus caching

The runtime documentation source (the crawled corpus or downloaded index) is built lazily and cached by
a singleton `IDocumentationSourceMaterializer`, keyed by the instance identifier. The cache is rebuilt
only when the instance changes, so an edit to a site's settings is picked up on the next search while an
unchanged instance reuses its corpus across calls.

## Adding a new documentation source

To support a documentation site that none of the built-in strategies cover, implement a new
[tool instance source](../core/tool-instances.md):

1. Create a settings model for the user-provided configuration.
2. Implement `IAIToolInstanceSource.CreateTool(AIToolInstance)` to read the settings and return a
   `DocumentationSearchToolFunction` (or your own `AIFunction`) bound to a concrete `IDocumentationSource`.
3. Register the source on the tool instances builder with `AddSource<TSource>(name, configure)`.

Operators then create instances of your new source exactly like the built-in ones, and expose them
through the same MCP allow-list.

## How It Works

1. `AddDocumentationSearchSources()` registers the three tool instance sources, a singleton
   `IDocumentationSourceMaterializer`, and a named `HttpClient` with standard resilience.
2. An operator configures one or more `AIToolInstance` entries, each bound to a single site through its
   settings.
3. When the MCP server lists or calls tools, allow-listed instances are materialized through their
   keyed `IAIToolInstanceSource`, which produces a `DocumentationSearchToolFunction`.
4. On invocation, the function resolves (and caches) the concrete `IDocumentationSource`, searches it,
   and returns the ranked results with their titles and URLs so the model can cite them.
