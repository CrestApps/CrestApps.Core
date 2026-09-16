# RAG and ingestion — outstanding work

One place for everything still owed on the ingestion branch. Fixed defects are described in the
changelog rather than here; the row is deleted from this file once the change ships. The review record
that produced the original defect list is
[pdf-ingestion-review-findings.md](pdf-ingestion-review-findings.md).

**One fact that governs the redesign:** the entire `Ingested` subsystem is *uncommitted* on this branch —
`CrestApps.Core.AI.Indexers`, `.Indexers.FileTransfer`, `Documents/Knowledge`, `Documents/Ingestion`,
`Mcp.Knowledge`, both knowledge stores, both docs pages and their tests are all untracked. Renaming
`Ingested` to `File` therefore costs no migration of released data — only of a local dev database.

## Fixed in this pass

Sixteen of the eighteen tracked defects are closed. Build clean, 3,616 tests passing, docs build green,
both sample hosts start.

The blocking one is worth stating plainly because its blast radius was far wider than the symptom that
found it. `ConfigurationAIDeploymentSource` synthesized a connection-default chat deployment declaring
`textGeneration` only. `ModelFeaturesAICompletionServiceHandler` documents enforcement as opt-in — "only
deployments that declare their capability metadata constrain the request" — but a synthesized deployment
*always* carries that metadata, so the opt-in was defeated and enforcement was mandatory. The record is
`IsReadOnly`, so no operator could correct it. Every request on such a deployment silently lost **every
tool** and had streaming downgraded to a single response. Chat interactions appeared to work only because
setting `DataSourceId` activates preemptive RAG, which injects retrieved content into the system message
with no tool call at all.

## Still open

| # | Status | Issue | Where |
| --- | --- | --- | --- |
| 1 | open | The `[chart:…]` marker parser is duplicated: the shared chat asset and a hand-inlined copy in the MVC chat-interaction view, each with its own id prefix and config map. Two implementations of one contract will drift. Deduplicating means regenerating the gulp assets and the committed `.map` files, so it was held back from the batch fix. | `Assets/js/ai-chat.js:418` and `Areas/ChatInteractions/Views/ChatInteraction/Chat.cshtml:830` |
| 2 | open | **A search whose index is unreachable is reported to the model as "no results".** Every content manager catches its own provider exception, logs it, and returns an empty list, so a dead database, a bad credential and a genuine miss are indistinguishable upstream. Observed live: with PostgreSQL stopped, the model answered "I could not find a photograph in this issue" — a confident wrong answer where the truth was "the index is down". Retrieval needs to distinguish failure from emptiness and say so. | `PostgreSQL/Services/PostgreSQLDataSourceContentManager.cs`, and the Elasticsearch and Azure equivalents |
| 3 | open | **An index sync that fails leaves the data source silently empty, and nothing in the UI says so.** Observed live: a file was ingested while the vector database was stopped. The knowledge objects were written (126 figures, 339 text chunks), the sync to the index failed, and the data source then reported a normal, healthy state with zero indexed rows — so every question about it was answered "not found" for days. A data source needs a visible last-sync outcome, and a failed sync needs to be retried or surfaced. | `AI/Services/DefaultAIDataSourceIndexingService.cs` |
| 4 | open | A capability strip is invisible to the operator. It logs a warning naming the deployment, feature and tool count, but nothing surfaces in the admin UI or the chat, so an under-declared deployment reads as "the AI is just bad at this" rather than a configuration problem. | `Capabilities/ModelFeatureEnforcement.cs:38` |
| 5 | open | Chart series still have no names. The split by polyline identity is done, so two plotted lines now yield two series, but naming them needs legend parsing — matching a swatch's colour or dash pattern to a label — and neither colour nor stroke style is carried on the segment type. No name is invented in the meantime. | `Pdf/Services/VectorPathChartDataExtractor.cs` |
| 6 | open | `AIDataSourceRagMetadata` has no `ContentTypes` member, so a data source attached directly to a profile cannot be restricted to particular knowledge kinds the way a tool instance can through `DataSourceSearchToolSettings.ContentTypes`. | `Models/AIDataSourceRagMetadata.cs` |
| 7 | open | The YesSql knowledge-object store's figure-hash lookup has no unit test. The suite's YesSql harness hardwires the three chat index providers it registers, so covering `KnowledgeObjectIndex` means extending that harness. The EntityCore side is covered. | `tests/…/Framework/Mvc/YesSqlAIStoreTestDatabase.cs` |
| 8 | open | `ModelFeaturesAICompletionServiceHandler`'s XML doc still frames enforcement as opt-in without noting that a source which always emits metadata makes it mandatory for its deployments. Accurate as written, but it reads as reassurance that does not hold — it is what made the under-declaration look harmless. | `Handlers/ModelFeaturesAICompletionServiceHandler.cs` |

## Needs your action, not code

**A local-folder ingester reads only from an allowed root.** The empty default is deliberate — an allowed
root lets anyone with the indexer screen read any file under it that the host can open — so both hosts ship
with the section documented and empty. A development root is now set in user secrets for both hosts,
pointing at a dedicated folder rather than a whole desktop:

```bash
dotnet user-secrets set "CrestApps:Indexers:AllowedLocalRoots:0" "D:\ingestion-source"
```

**The end-to-end chat check now passes.** Driven through the MVC host: the model calls the tool, the
search returns figures, and the answer embeds a real image served from the running host with its caption
translated and its printed page named. Verified visually, not just from logs.

**One more defect was found by that run and fixed: the strictness floor was unreachable.**
`GetMinimumScore` mapped strictness onto `(level - 1) / levels`, demanding 0.4 at the default level and 0.8
at the narrowest. Cosine similarity does not use that range. Measured against the real corpus, the best
score any query achieved — including one naming exactly what the top-ranked figure depicted — was about
0.27. Every level from the default upwards was therefore unreachable and retrieval returned nothing for
every query, reported as an ordinary empty result. Ranking was never wrong; the floor was. The scale now
interpolates from zero up to a configurable `StrictestMinimumScore` (default 0.4), so the levels are
0.0, 0.1, 0.2, 0.3, 0.4.

**And a usability trap was closed.** A tool instance named `magazine_search` is presented to the model as
`tool_instance_magazine_search`, so a system message telling the model to call it by its display name names
a function that does not exist — the instruction cannot bind, and tool choice silently falls back to
description matching. Both hosts' tool instance screens now show the real function name, and the Blazor
hint that claimed the display name *was* the function name is corrected.

## Requested redesign

### A — A `File` data source fed by file sources — DONE

The source type is renamed (`AIDataSourceSourceTypes.File == "File"`) across 32 files, including the
hard-coded JavaScript literal that would otherwise have made a `File` data source silently unsaveable at
runtime. The figure storage segment stays pinned to the literal `"Ingested"` behind a named constant: a
source type is a UI-facing name that may be renamed, a storage path is written into every stored object and
read back verbatim, and deriving one from the other means a later rename strands older figures under the
old prefix.

A dedicated **File Sources** screen now exists in both hosts, backed by the existing `WebCrawler` record —
a file source is simply one whose `Source` is an ingestion connector rather than a crawl strategy, and the
two screens filter on exactly that. Web Crawlers is strategy-only again, the manual upload flow is retired,
and the docs page moved from `ingested.md` to `file.md`.

Verified in the running MVC host: the screen loads, the create form renders, the target dropdown offers only
`File` data sources, and choosing a connector shows only that connector's cards. All nine new source-parity
rows pass, so the two hosts cannot drift apart unnoticed again.

**Not verified:** the Blazor screen's rendering. Its route is registered and auth-gated correctly, but
confirming the page itself needs a signed-in session on that host.

### B — FTP and SFTP in their own projects

`Indexers.FileTransfer` references FluentFTP *and* SSH.NET *and* both MCP projects. Split on the
`Mcp.Ftp`/`Mcp.Sftp` precedent, with `IRemoteFileClient` and `RemoteFileIngestionConnector` — neither of
which references either library — staying in a neutral project both reference.

The catch: each host's shared `WebCrawlerViewModel` compiles against both protocols' metadata types and
connector names, so a host still takes both packages unless that view model is restructured too, which
undercuts the point of the split for anyone copying the sample.

Confirmed while scoping: `src/Primitives/CrestApps.Core.AI.Ftp` and `.../CrestApps.Core.AI.Sftp` contain
no project file at all — only stale `bin`/`obj`. Delete before reusing those names.

### C — Regrouped admin navigation, both hosts

| Section | Items |
| --- | --- |
| Admin | Dashboard, Articles |
| Artificial Intelligence | AI Connections, AI Deployments, Chat Interactions, AI Profiles, AI Tool Instances, Templates |
| RAG | File ingesters, Index Profiles, Data Sources, Web Crawlers |

Not started. Both sidebars are hand-written, fully duplicated lists — no shared partial, no nav model, no
registry — and all 19 items currently agree one-for-one, so the regrouping starts from a matched baseline
and must move in lockstep.

**Nothing compares navigation between the hosts.** The parity suite does not cover the layout files, so a
one-sided regrouping would ship silently. Adding them is possible but the pair is asymmetric: the MVC file
also holds the top navbar, the validation alert and the chat-widget include.

Open questions: the hosts use *different* section-label markup today (`<h6>` for Admin, a disabled `<span>`
for Reports) and one convention must win; four items (A2A Hosts, MCP Hosts, MCP Prompts, MCP Resources)
plus Settings sit in unlabeled divider groups the target grouping does not name; and placing RAG directly
below Admin pushes Reports below Artificial Intelligence, which should be confirmed.

### D — Charts as data, not pictures

**Chart.js v4 and a wire contract already exist** — the `[chart:{…Chart.js config…}]` marker, emitted by
`GenerateChartTool` and consumed by a `marked` block extension with a brace-balanced parser, canvas
emission, a PNG download button, DOMPurify configured for `<canvas>`, rAF deferral, destroy-before-recreate
and a retry queue for hidden containers. Reuse it; do not invent a second contract.

Extraction has caught up: series now split by polyline, and axis titles and chart type reach `ChartDetails`
where they can be honestly determined. What remains is the transport — the series is persisted on the
knowledge object but still never indexed and never reaches the model. The read-one-object tool already
returns a table's rows as JSON; the chart branch stops at the confidence line. That table branch is the
precedent.

**Any renderer must refuse to plot anything below `Exact` confidence.** The three-level model exists
specifically to stop estimated values being stored as facts, and a Chart.js canvas presents whatever it is
given as measured data.

### E — Tools over typed knowledge

The system `search_data_sources` tool now takes `contentTypes`, so both paths can be narrowed to figures or
charts. Still missing: nothing can enumerate the charts or tables in a document — there is no list-by-type
tool, only vector search — and walking from a text hit to its parent article or sibling figures is
unsupported even though `rootId` and `parentId` are indexed.

## Not gating

Two rows of the sample-host source-parity suite were already failing before this branch, on screens it
never touched. That suite runs nightly rather than on pull requests.
