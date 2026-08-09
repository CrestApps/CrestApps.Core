---
sidebar_label: Documentation Search
sidebar_position: 5
title: Documentation Search
description: Expose an opt-in AI tool that searches one or more public documentation sites as a knowledge base.
---

# Documentation Search

> Register an opt-in AI tool that searches one or more configured documentation sites (such as Docusaurus or MkDocs) and returns the most relevant passages with their source URLs.

## Problem & Solution

A knowledge-base MCP server often needs to answer questions from product or framework documentation
that lives on public sites. Instead of indexing that content into a vector store, the documentation
search tool lets you declare a set of documentation sites and scan them on demand. The tool is
**opt-in** — it is not registered by any default AI registration, so it only appears when you call
`AddCoreAIDocumentationSearch(...)` (or the `AddDocumentationSearch(...)` builder method). This makes
it a good fit for a read-only knowledge-base server that should search documentation but perform no
actions.

## Quick Start

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddMcpServer(mcpServer => mcpServer
            .AddYesSqlStores()
            .AddDocumentationSearch(docs => docs
                .AddSite("crestapps", "https://core.crestapps.com")
                .AddSite("orchardcore", "https://docs.orchardcore.net")
            )
        )
    )
);
```

Or register it directly on the service collection:

```csharp
builder.Services.AddCoreAIDocumentationSearch(docs => docs
    .AddSite("crestapps", "https://core.crestapps.com"));
```

The tool is registered under the name `search_documentation` with the category `knowledgebase` and the
purpose `data_source_search`. Because it carries a category, a knowledge-base MCP server can expose it
selectively:

```csharp
_ = builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithCrestAppsHandlers(handlers => handlers
        .WithToolsInCategory(DocumentationSearchFunction.Category));
```

See [MCP Server](./server.md#selecting-which-capabilities-to-expose) for capability and tool selection.

## Configuring Sites

Sites can be declared in code with `AddSite(...)`, or bound from configuration by configuring
`DocumentationSearchOptions`:

```csharp
builder.Services.AddCoreAIDocumentationSearch();
builder.Services.Configure<DocumentationSearchOptions>(
    builder.Configuration.GetSection("DocumentationSearch"));
```

```json
{
  "DocumentationSearch": {
    "MaxResultsPerSite": 5,
    "MaxPagesPerSite": 200,
    "CacheDuration": "01:00:00",
    "Sites": [
      { "Name": "crestapps", "BaseUrl": "https://core.crestapps.com", "Kind": "docusaurus" },
      { "Name": "orchardcore", "BaseUrl": "https://docs.orchardcore.net", "Kind": "mkdocs" }
    ]
  }
}
```

The built-in crawler discovers pages through the site's `sitemap.xml`. Supply `SitemapUrl` on a site to
override the default `{BaseUrl}/sitemap.xml` location. Both Docusaurus and MkDocs publish a standard
sitemap, so the `Kind` value is a hint only.

### DocumentationSearchOptions

| Property | Default | Description |
|----------|---------|-------------|
| `Sites` | empty | The public documentation sites the crawler scans. |
| `MaxResultsPerSite` | `5` | Default maximum results a single site contributes to a search. |
| `MaxPagesPerSite` | `200` | Default maximum pages the crawler indexes per site. |
| `MaxConcurrentRequests` | `4` | Maximum concurrent page requests per site while crawling. |
| `CacheDuration` | `1 hour` | How long a crawled site corpus is cached before it is refreshed. |

### DocumentationSite

| Property | Description |
|----------|-------------|
| `Name` | Unique logical name; a caller can scope a search to this source. |
| `BaseUrl` | Base URL of the documentation site. |
| `SitemapUrl` | Optional explicit sitemap URL. |
| `Kind` | Optional generator hint (for example `docusaurus` or `mkdocs`); any custom string is allowed. |
| `MaxResults` | Optional per-site override for the maximum results. |
| `MaxPages` | Optional per-site override for the maximum indexed pages. |

The first search against a site crawls its pages and caches the corpus in memory for `CacheDuration`;
subsequent searches reuse the cache. Ranking uses lightweight keyword scoring.

## Custom Sources

Implement `IDocumentationSource` to search anything that is not a public sitemap-based site (for
example a local corpus, a search API, or a vector index) and register it with `AddSource`:

```csharp
public sealed class MyDocsSource : IDocumentationSource
{
    public string Name => "my-docs";

    public Task<IReadOnlyList<DocumentationSearchResult>> SearchAsync(
        DocumentationSearchRequest request,
        CancellationToken cancellationToken)
    {
        // Return matches ordered by descending Score.
    }
}
```

```csharp
.AddDocumentationSearch(docs => docs
    .AddSource<MyDocsSource>()
    .AddSite("crestapps", "https://core.crestapps.com"))
```

Custom sources and configured sites are aggregated by `IDocumentationSourceProvider`. When the model
calls the tool without a `source` argument, every source is searched and results are merged by score;
passing a `source` argument scopes the search to that single named source.

## How It Works

1. `AddCoreAIDocumentationSearch(...)` registers the `search_documentation` tool, the
   `DefaultDocumentationSourceProvider`, and a named `HttpClient` with standard resilience.
2. When invoked, the tool resolves `IDocumentationSourceProvider` to get all sources (custom sources
   plus a `SitemapDocumentationSource` per configured site).
3. Each source is searched in parallel; a failing source is skipped so one broken site does not fail
   the whole search.
4. Results are merged, ordered by descending relevance, and returned with their titles and URLs so the
   model can cite them.
