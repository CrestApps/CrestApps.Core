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
      { "Name": "crestapps", "BaseUrl": "https://core.crestapps.com" },
      { "Name": "orchardcore", "BaseUrl": "https://docs.orchardcore.net" }
    ]
  }
}
```

The built-in crawler discovers pages through the site's `sitemap.xml`. Supply `SitemapUrl` on a site to
override the default `{BaseUrl}/sitemap.xml` location. Both Docusaurus and MkDocs publish a standard
sitemap, so no generator-specific configuration is required.

### DocumentationSearchOptions

| Property | Default | Description |
|----------|---------|-------------|
| `Sites` | empty | The public documentation sites the crawler scans through their sitemap. |
| `SearchIndexes` | empty | The documentation sites that publish a prebuilt search index as JSON. |
| `AlgoliaSources` | empty | The documentation sites searchable through the Algolia DocSearch API. |
| `MaxResultsPerSite` | `5` | Default maximum results a single site contributes to a search. |
| `MaxPagesPerSite` | `200` | Default maximum pages the crawler indexes per site. |
| `MaxConcurrentRequests` | `4` | Maximum concurrent page requests per site while crawling. |
| `CacheDuration` | `1 hour` | How long a crawled or downloaded site corpus is cached before it is refreshed. |

### DocumentationSite

| Property | Description |
|----------|-------------|
| `Name` | Unique logical name; a caller can scope a search to this source. |
| `BaseUrl` | Base URL of the documentation site. |
| `SitemapUrl` | Optional explicit sitemap URL. |
| `MaxResults` | Optional per-site override for the maximum results. |
| `MaxPages` | Optional per-site override for the maximum indexed pages. |

The first search against a site crawls its pages and caches the corpus in memory for `CacheDuration`;
subsequent searches reuse the cache. Ranking uses lightweight keyword scoring.

## Search Strategies

A single documentation site can be indexed in different ways depending on what the generator publishes.
Each strategy has its own builder method and configuration model, so you choose the one that matches the
site — there is no single "kind" switch to configure.

| Strategy | Builder method | Best for | How it works |
|----------|----------------|----------|--------------|
| Sitemap crawl | `AddSite(...)` | Any site that publishes `sitemap.xml` (Docusaurus, MkDocs, and most static sites). | Crawls pages, strips HTML, and ranks locally with keyword scoring. |
| Search index | `AddSearchIndex(...)` | MkDocs Material and other sites that publish a fetchable `search_index.json`. | Downloads the prebuilt index once and ranks its entries locally. |
| Algolia DocSearch | `AddAlgoliaDocSearch(...)` | Docusaurus sites (and others) wired to hosted Algolia DocSearch. | Forwards the query to Algolia, which performs the ranking. |

### Example: a public Docusaurus site

A public Docusaurus site that requires no authentication — such as
[core.crestapps.com](https://core.crestapps.com) — only needs the sitemap crawl strategy. Docusaurus
publishes a standard `sitemap.xml` at the site root, so `AddSite(...)` is all that is required: give the
source a logical name and the site's base URL, and the crawler discovers `{BaseUrl}/sitemap.xml`
automatically.

```csharp
builder.Services.AddCoreAIDocumentationSearch(docs => docs
    .AddSite("crestapps-core", "https://core.crestapps.com"));
```

Or on the MCP server builder for a read-only knowledge-base server:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddMcpServer(mcpServer => mcpServer
            .AddYesSqlStores()
            .AddDocumentationSearch(docs => docs
                .AddSite("crestapps-core", "https://core.crestapps.com")
            )
        )
    )
);
```

You can tune how much of the site is indexed and scope results with the optional `configure` action:

```csharp
.AddDocumentationSearch(docs => docs
    .AddSite("crestapps-core", "https://core.crestapps.com", site =>
    {
        // Only needed if the sitemap is not at {BaseUrl}/sitemap.xml.
        site.SitemapUrl = "https://core.crestapps.com/sitemap.xml";
        site.MaxPages = 300;   // Cap the number of pages crawled.
        site.MaxResults = 5;   // Cap the results this site contributes per search.
    }))
```

The same site can also be declared in configuration instead of code:

```json
{
  "DocumentationSearch": {
    "Sites": [
      { "Name": "crestapps-core", "BaseUrl": "https://core.crestapps.com" }
    ]
  }
}
```

Because the site is public, no headers, API keys, or credentials are involved — the crawler issues
plain anonymous `GET` requests through the source's resilient `HttpClient`. The first search crawls the
site and caches the corpus for `CacheDuration`; later searches reuse the cache.

:::tip
Prefer the sitemap crawl for a public Docusaurus site. Only reach for `AddAlgoliaDocSearch(...)` when
the site is wired to hosted Algolia DocSearch and you have its application ID, search-only API key, and
index name.
:::

### Search Index Source

MkDocs Material publishes a fetchable `search_index.json`. `AddSearchIndex(...)` downloads that index
once, caches it for `CacheDuration`, and ranks its entries with the same keyword scoring as the crawler
— without fetching every page individually.

```csharp
.AddDocumentationSearch(docs => docs
    .AddSearchIndex("mkdocs", "https://www.mkdocs.org", site =>
    {
        // Optional. Defaults to {BaseUrl}/search/search_index.json.
        site.IndexUrl = "https://www.mkdocs.org/search/search_index.json";
        site.MaxResults = 5;
    }))
```

| `DocumentationSearchIndexSite` property | Description |
|----------|-------------|
| `Name` | Unique logical name; a caller can scope a search to this source. |
| `BaseUrl` | Base URL used to resolve relative entry locations and the default index URL. |
| `IndexUrl` | Optional explicit index URL. Defaults to `{BaseUrl}/search/search_index.json`. |
| `MaxResults` | Optional per-site override for the maximum results. |

:::note
This targets the MkDocs Material `search_index.json` schema (`{ "docs": [ { "location", "title", "text" } ] }`).
Docusaurus' `@easyops-cn/docusaurus-search-local` plugin stores a client-side Lunr index that is not a
cleanly fetchable JSON document, so use the sitemap crawl or Algolia DocSearch for Docusaurus sites.
:::

### Algolia DocSearch Source

Many Docusaurus sites use hosted Algolia DocSearch rather than a fetchable index. `AddAlgoliaDocSearch(...)`
forwards each query to the Algolia query API and maps the returned hits to results. Because Algolia
performs the ranking, this source issues a live query per search and does not crawl or cache a corpus.

```csharp
.AddDocumentationSearch(docs => docs
    .AddAlgoliaDocSearch(
        name: "docusaurus",
        applicationId: "YOUR_APP_ID",
        apiKey: "YOUR_SEARCH_ONLY_API_KEY",
        indexName: "your-index",
        site => site.MaxResults = 5))
```

| `AlgoliaDocSearchSite` property | Description |
|----------|-------------|
| `Name` | Unique logical name; a caller can scope a search to this source. |
| `ApplicationId` | Algolia application identifier. |
| `ApiKey` | Algolia **search-only** API key (never a write key). |
| `IndexName` | Algolia index name to query. |
| `MaxResults` | Optional per-site override for the maximum results. |

Each strategy also binds from configuration through the matching `SearchIndexes` and `AlgoliaSources`
lists on `DocumentationSearchOptions`, mirroring the `Sites` list shown above.

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

## Store-backed Sources

Sites registered with `AddSite`, `AddSearchIndex`, and `AddAlgoliaDocSearch` are defined in code (or
bound from configuration). If you want operators to add, edit, and remove documentation sources at
runtime — through an admin UI or directly in the database — persist them in a store instead.

Add a store backend to the documentation search builder:

```csharp
// YesSql
.AddDocumentationSearch(docs => docs
    .AddYesSqlStores())

// Entity Framework Core
.AddDocumentationSearch(docs => docs
    .AddEntityCoreStores())
```

Store-backed sources and code/options-defined sources are aggregated together, so you can seed a few
sites in code and let operators manage the rest from the database.

Each stored source is a `DocumentationSourceEntry`. The `Strategy` field (stored in `Source`) selects
how the entry is materialized and must match a registered `IDocumentationSourceFactory.Strategy`. The
built-in strategies are defined by `DocumentationSourceStrategies`:

| Strategy (`DocumentationSourceStrategies`) | Value | Required fields |
| --- | --- | --- |
| `Sitemap` | `sitemap` | `BaseUrl` (optional `SitemapUrl`, `MaxResults`, `MaxPages`) |
| `SearchIndex` | `search-index` | `BaseUrl` (optional `IndexUrl`, `MaxResults`) |
| `Algolia` | `algolia` | `ApplicationId`, `ApiKey`, `IndexName` (optional `MaxResults`) |

Create and manage entries through the named-source catalog manager, which validates the strategy and
its required fields and enforces a unique name:

```csharp
public sealed class DocumentationSourceService
{
    private readonly INamedSourceCatalogManager<DocumentationSourceEntry> _manager;

    public DocumentationSourceService(INamedSourceCatalogManager<DocumentationSourceEntry> manager)
    {
        _manager = manager;
    }

    public async Task AddSitemapAsync(string name, string baseUrl, CancellationToken cancellationToken)
    {
        var entry = await _manager.NewAsync(name, DocumentationSourceStrategies.Sitemap, cancellationToken: cancellationToken);
        entry.BaseUrl = baseUrl;

        var validation = await _manager.ValidateAsync(entry, cancellationToken);

        if (validation.Succeeded)
        {
            await _manager.CreateAsync(entry, cancellationToken);
        }
    }
}
```

The provider rebuilds a stored source only when its entry changes (tracked by the entry's modified
timestamp), so an edit in the database is picked up on the next search without restarting the host.

To register a new strategy that can be stored in the catalog, implement `IDocumentationSourceFactory`
with a new `Strategy` identifier and register it as an `IDocumentationSourceFactory`.

## How It Works

1. `AddCoreAIDocumentationSearch(...)` registers the `search_documentation` tool, the
   `DefaultDocumentationSourceProvider`, the strategy factories, the documentation source catalog and
   manager, and a named `HttpClient` with standard resilience.
2. When invoked, the tool resolves `IDocumentationSourceProvider` to get all sources: custom sources,
   the options-defined sites, and any entries persisted in the documentation source catalog — each
   materialized through its `IDocumentationSourceFactory` (a `SitemapDocumentationSource`,
   `SearchIndexDocumentationSource`, or `AlgoliaDocumentationSource`).
3. Each source is searched in parallel; a failing source is skipped so one broken site does not fail
   the whole search.
4. Results are merged, ordered by descending relevance, and returned with their titles and URLs so the
   model can cite them.
