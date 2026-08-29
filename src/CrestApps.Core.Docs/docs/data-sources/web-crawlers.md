---
sidebar_label: Web Crawlers
sidebar_position: 6
title: Web Crawlers
description: Scrape public websites into a Web AI data source with pluggable crawl strategies, starting with sitemap discovery.
---

# Web Crawlers

> Populate an AI knowledge base directly from public websites. A **web crawler** picks a scraping
> **strategy**, supplies that strategy's settings, and points at a **Web** data source. Many crawlers can
> map many sites into a single knowledge base, and each is re-crawled on its own schedule.

Web crawlers live in the opt-in `CrestApps.Core.AI.WebCrawlers` package and are managed through the
**Web Crawlers** area of the sample hosts.

## How it fits together

```text
Web Crawler (strategy: Sitemap) ─┐
Web Crawler (strategy: Sitemap) ─┼──▶  Web AI Data Source  ──▶  Knowledge-base index (chunks + embeddings)
Web Crawler (future strategy)   ─┘
```

- A **Web** AI data source (source type `Web`) is a plain target bucket — no site configuration, no field
  mapping. Create it under **Data Sources** and choose the **Web** source type.
- A **web crawler** record chooses a **strategy** (its `Source`, for example `Sitemap`), configures that
  strategy (base/sitemap URL, page limits, URL filters), and selects the target **Web** data source.
- The crawler's pages are fetched, cleaned to text, chunked, embedded, and stored in the data source's
  knowledge base with the page URL retained for citations.

Because the data source is strategy-agnostic, new strategies (for example depth-limited link following)
can be added later by implementing one interface — without changing the data source or the UI.

## Strategies

| Strategy | What it does |
| --- | --- |
| `Sitemap` | Discovers pages through the site's sitemap(s) — flat `urlset`, nested `sitemapindex`, gzip and plain-text sitemaps, RSS/Atom feeds, and `robots.txt` advertisements — then fetches and cleans each page. |

Each page's HTML is cleaned into plain text through a reusable
`CrestApps.Core.DataIngestion.HtmlIngestionDocumentReader` (a `Microsoft.Extensions.DataIngestion`
reader that strips scripts, styles, and markup), and the resulting text is normalized and token-chunked by
the shared data-source indexing pipeline.

### Sitemap settings

| Setting | Purpose |
| --- | --- |
| Base URL | Resolves the sitemap through `robots.txt` and the conventional locations when no explicit sitemap URL is given. |
| Sitemap URL | An explicit sitemap or sitemap-index URL to start discovery from. |
| Max pages | Caps how many pages are scraped from the site. |
| Max concurrent requests | Bounds parallel page fetches. |
| Request timeout (seconds) | Per-page fetch timeout. |
| User agent | The `User-Agent` header presented while crawling. |
| Include / Exclude URL patterns | Optional regular expressions; a page must match an include (when any are set) and must not match an exclude. |

A base URL or an explicit sitemap URL is required.

## Re-indexing

Each crawler has its own **re-index interval**. A background service (`WebCrawlerReindexBackgroundService`)
periodically re-crawls each due site and enqueues only the pages that changed:

- **New** pages (not seen before) are queued for indexing.
- **Changed** pages — those whose sitemap `<lastmod>` is newer than the recorded value — are re-fetched
  and re-embedded.
- **Removed** pages (missing from the crawl) are deleted from the knowledge base.

Pages without a `<lastmod>` timestamp are refreshed by the framework's nightly full data-source alignment
rather than the incremental pass. Re-index diffing and scheduling are exposed as the injectable
`IWebCrawlerReindexPlanner`, so a host can drive them from its own scheduler.

## Citations

The scraped page URL is stored as each chunk's reference id, and a keyed `IAIReferenceLinkResolver` returns
that URL as the citation link, so chat answers grounded in scraped content link back to the original page.

## Registration

Both sample hosts opt in. Register the feature and a persistence backend:

```csharp
// Feature services (source handler, strategies, planner, background service).
builder.Services.AddCoreWebCrawlers();

// Persistence: choose the backend that matches your data stores.
builder.Services.AddCoreWebCrawlerStoresYesSql();      // YesSql
// builder.Services.AddCoreWebCrawlerStoresEntityCore(); // EntityFramework Core
```

`AddCoreWebCrawlers()` registers the `Web` data source source handler and citation link resolver, the crawl
strategies, the re-index planner and its background service, and the crawler catalog handler.

## Extending with a new strategy

Implement `IWebCrawlerStrategy` (discover page references, fetch and clean a page, validate the crawler's
settings), register it keyed by its identifier, and add a descriptor to `WebCrawlerStrategyOptions` so it
appears in the strategy dropdown. No data-source or UI changes are required.
