# RAG and ingestion — outstanding work

One place for everything still owed on the ingestion branch. Fixed defects are described in the
changelog rather than here; the row is deleted from this file once the change ships. The review record
that produced the original defect list is
[pdf-ingestion-review-findings.md](pdf-ingestion-review-findings.md).

**The fact that used to govern this file has expired.** The `Ingested` subsystem is no longer
uncommitted: it landed as `ecdd8021` on `ma/file-sources-and-figure-retrieval`, and the search-failure
fix followed as `e4796fc5`. The branch is still unmerged and unreleased, so a schema change costs no
migration of released data — only of a local dev database — but it is no longer true that the work can
be reshaped without rewriting history.

## State of the branch

Build clean on both sample hosts, **3,688 tests passing**, `gulp rebuild` idempotent. Work sits in two
places, and the distinction matters when reading the rows below:

- **Committed** — the subsystem itself, the `File` rename and its screens, the FTP/SFTP project split,
  the regrouped navigation, and failure-vs-emptiness in retrieval.
- **In the working tree, not yet committed** — the sync summary, the capability-strip surfacing, the
  chart-marker deduplication and its regenerated assets, the enforcement documentation, the chart
  series reaching the model, and the list-by-type tool.

## Fixed since the last pass

Five of the eight rows that were open are closed, and two of the three redesigns recorded here as
unstarted turned out to be done.

The one worth restating is failure-vs-emptiness, because its blast radius was wider than the symptom
that found it. Every content manager caught its own provider exception, logged it and returned an empty
list, so a dead index, a bad credential and a genuine miss were indistinguishable upstream. With
PostgreSQL stopped the model answered "I could not find a photograph in this issue" — a confident wrong
answer where the truth was "the index is down". Content managers now offer `TrySearchAsync` alongside
`SearchAsync`, with a default implementation so providers outside this repository keep compiling, and a
search that failed no longer produces the message a search that matched nothing produces.

## Still open

| # | Status | Issue | Where |
| --- | --- | --- | --- |
| 1 | open | Chart series still have no names. The split by polyline identity is done, so two plotted lines now yield two series, but naming them needs legend parsing — matching a swatch's colour or dash pattern to a label — and neither colour nor stroke style is carried on the segment type. No name is invented in the meantime. | `Pdf/Services/VectorPathChartDataExtractor.cs:300` |
| 2 | open | `AIDataSourceRagMetadata` has no `ContentTypes` member, so a data source attached directly to a profile cannot be restricted to particular knowledge kinds the way a tool instance can through `DataSourceSearchToolSettings.ContentTypes`. | `Models/AIDataSourceRagMetadata.cs` |
| 3 | open | The YesSql knowledge-object store's figure-hash lookup has no unit test. The suite's YesSql harness hardwires the three chat index providers it registers, so covering `KnowledgeObjectIndex` means extending that harness. The EntityCore side is covered. | `tests/…/Framework/Mvc/YesSqlAIStoreTestDatabase.cs` |
| 4 | open | **Nothing compares the two hosts' navigation.** The regrouping below shipped in lockstep and the two sidebars agree today, but the parity suite does not cover the layout files, so the next one-sided edit ships silently. The pair is asymmetric — the MVC file also holds the top navbar, the validation alert and the chat-widget include — so a comparison has to name what it compares rather than diff the files. | `Views/Shared/_Layout.cshtml`, `Components/Layout/NavMenu.razor` |
| 5 | open | **The JavaScript marker tests never run anywhere but a developer's shell.** `chart-markers.test.js` (15 cases), `figure-markers.test.js` (14) and `citation-markers.test.js` (35) pass under `node --test`, but `npm test` runs only `test:gate`, which names `gate-decision.test.js` alone, and no workflow runs any node test at all — `pr_ci.yml` runs `dotnet test` and nothing else. The rules these cover are now shared by three chat surfaces, so they are exactly the code that should be gated. Pre-existing, and wider than this branch. | `package.json`, `.github/workflows/pr_ci.yml` |
| 6 | open | **A model told a chart is machine-readable still shows a picture of it.** Driven live: asked whether a chart's values were machine-readable, the model answered that they were, described the numeric series — and rendered the figure as an image. The page had `canvasCount: 0` with the marker parser and Chart.js both loaded and ready. The over-claim is the model reading the series it was given; the contradiction is D below, seen from the reader's side rather than the code's. | see D |
| 7 | open | SignalR is loaded from `cdn.jsdelivr.net/npm/@microsoft/signalr@latest`. A floating version cannot carry an SRI hash and cannot be reproduced, in a sample host others copy. | both hosts' chat views |

## Needs your action, not code

**A local-folder ingester reads only from an allowed root.** The empty default is deliberate — an allowed
root lets anyone with the indexer screen read any file under it that the host can open — so both hosts ship
with the section documented and empty. A development root is now set in user secrets for both hosts,
pointing at a dedicated folder rather than a whole desktop:

```bash
dotnet user-secrets set "CrestApps:Indexers:AllowedLocalRoots:0" "D:\ingestion-source"
```

**The Blazor File Sources screen has still never been looked at.** Its route is registered and auth-gated
correctly and its parity rows pass, but confirming the page renders needs a signed-in session on that host.
The MVC screen was driven live and is sound — it lists the local-folder ingestor with its last run, and Web
Crawlers correctly lists only the sitemap strategy — so what is unverified is the Blazor rendering alone.

**A data source that is demonstrably indexed still reports its last sync as "Never".** Seen live: the
magazine answers questions from its 170 stored objects while the Data Sources screen reads `Never` for it.
The recording path looks right, and the likeliest explanation is simply that the data predates the summary,
in which case one sync settles it. Worth confirming rather than assuming, since the alternative is that the
feature does not record anything and the screen is reassuring about a source that was never indexed.

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
rows pass. The Blazor screen remains unseen, as noted above.

### B — FTP and SFTP in their own projects — DONE

`CrestApps.Core.AI.Indexers.Ftp` and `.Indexers.Sftp` now exist as separate projects and
`.Indexers.FileTransfer` is gone, so a host takes FluentFTP or SSH.NET rather than both. `IRemoteFileClient`
and `RemoteFileIngestionConnector` reference neither library and stay in the neutral `Indexers` project that
both reference. The stale `CrestApps.Core.AI.Ftp` and `.Sftp` directories — which held only `bin`/`obj` and
no project file — were deleted rather than reused.

The catch recorded when this was scoped still stands: each host's shared `WebCrawlerViewModel` compiles
against both protocols' metadata types, so the sample hosts themselves still take both packages. The split
achieves its purpose for anyone consuming the projects, not yet for anyone copying the sample.

### C — Regrouped admin navigation, both hosts — DONE

| Section | Items |
| --- | --- |
| Admin | Dashboard, Articles |
| Artificial Intelligence | AI Connections, AI Deployments, Chat Interactions, AI Profiles, AI Tool Instances, Templates |
| RAG | File Sources, Web Crawlers, Index Profiles, Data Sources |

Both sidebars carry the two labelled sections and agree item for item. The open question about markup was
settled in favour of the disabled `<span>` convention, used for every section label in both hosts, so the
`<h6>` form is gone.

What did not come with it is any guard against drift — that is row 4 above, and it is the reason this entry
is "done" rather than "closed".

### D — Charts as data, not pictures

**Chart.js v4 and a wire contract already exist** — the `[chart:{…Chart.js config…}]` marker, emitted by
`GenerateChartTool` and consumed by a `marked` block extension with a brace-balanced parser, canvas
emission, a PNG download button, DOMPurify configured for `<canvas>`, rAF deferral, destroy-before-recreate
and a retry queue for hidden containers. Reuse it; do not invent a second contract.

**The transport is built, end to end.** A chart's numbers now survive the whole path: extracted from the
polylines, stored on the knowledge object, written onto the indexed row as a `series` field, read back by
retrieval, and inlined into the figures block the model is given. The read-one-object tool emits the chart
type, the axis titles and the series as JSON, exactly as the table branch emits a table's rows.

`Exact` confidence gates it at every stage independently — at the point the row is written and again at the
point the numbers are printed — so an estimate cannot reach a reader as a measurement by being forgotten
once. The inline block is capped at 24 points across 4 series and *says* what it left out, because a model
shown part of a chart as though it were the whole answers about the part with the confidence of the whole.

**What remains is the drawing.** Nothing turns those series into a `[chart:…]` marker, so a reader who asks
for a chart still gets a picture of one while the model, correctly, describes the values as machine-readable
— the contradiction in row 6. The parser and Chart.js are already loaded on the page and ready; what is
missing is the step that emits the marker.

Finding a chart *by* its numbers is a separate thing and is not in scope here: the series is stored as a
field rather than embedded, so it is retrievable once a chart is found, not a way of finding one.

**Any renderer must refuse to plot anything below `Exact` confidence.** The three-level model exists
specifically to stop estimated values being stored as facts, and a Chart.js canvas presents whatever it is
given as measured data. The tool already enforces this at the point of emission; a renderer must not assume
that is the only path.

### E — Tools over typed knowledge

The system `search_data_sources` tool takes `contentTypes`, so both paths can be narrowed to figures or
charts.

**Enumeration and traversal now exist**, in the working tree: `KnowledgeObjectListToolFunction` reads the
store directly rather than ranking against a query, because "what tables are in this issue?" is a question
about a set and a similarity search answers neither all of them nor a statement that it was all of them. It
walks `parent` and `siblings` relations, which closes the gap about reaching an article from one of its text
chunks.

It is new and uncommitted, and its behaviour has been exercised only by its unit tests — not against the
magazine through a host, the way the search path was.

## Not gating

Two rows of the sample-host source-parity suite were already failing before this branch, on screens it
never touched: the chat-interaction create page disagrees on one heading, and the settings page is missing
three realtime fields in the Blazor host. That suite runs nightly rather than on pull requests.
