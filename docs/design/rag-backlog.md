# RAG and ingestion — outstanding work

One place for everything still owed on the ingestion branch. Fixed defects are described in the
changelog rather than here; the row is deleted from this file once the change ships. The review record
that produced the original defect list is
[pdf-ingestion-review-findings.md](pdf-ingestion-review-findings.md).

## State of the branch

Everything described here is committed on `ma/file-sources-and-figure-retrieval`. Build clean on both sample
hosts, **3,703 tests passing**, 88 client-side unit tests passing, `gulp rebuild` idempotent, and the assets
gate green.

The fact that used to sit at the top of this file — that the subsystem was uncommitted and could be reshaped
for free — is gone twice over: the work is committed, and the working tree is empty.

## What closed, and what it cost

Of the eight rows this file opened with, seven are closed and one was withdrawn rather than fixed (see
**Decided, not owed**). Two of the three redesigns it recorded as unstarted turned out to have been done
already.

The one worth restating is failure-vs-emptiness, because its blast radius was wider than the symptom that
found it. Every content manager caught its own provider exception, logged it and returned an empty list, so a
dead index, a bad credential and a genuine miss were indistinguishable upstream. With PostgreSQL stopped the
model answered "I could not find a photograph in this issue" — a confident wrong answer where the truth was
"the index is down". Content managers now offer `TrySearchAsync` alongside `SearchAsync`, with a default
implementation so providers outside this repository keep compiling, and a search that failed no longer
produces the message a search that matched nothing produces.

## Still open

| # | Status | Issue | Where |
| --- | --- | --- | --- |
| — | — | Nothing open. |

## Decided, not owed

**Chart series are not named, and that is the answer rather than a gap.** Splitting by polyline identity is
done, so two plotted lines yield two series. Naming them would mean matching a legend swatch's colour or dash
pattern to a label, and neither is carried on the segment type. A name arrived at that way is a guess, and a
guess here does not read as one: it attributes measured values to a series that may not exist, which is the
exact failure the confidence levels were built to prevent. An unnamed series says what is known. This is
closed; do not re-open it as a to-do.

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

**The drawing is done too.** A chart read out of a document at `Exact` confidence now carries a
`[chart:…]` marker alongside its numbers, built from the series and drawn by the same parser the chart tool's
own output goes through — the existing contract, not a second one. A reader asking for a chart gets a chart.

It is a scatter with the line shown rather than a line chart, because the points carry their own x values
read off the axis and a line chart would space them evenly and quietly redraw the data. No legend is drawn
when no series was named: "Dataset 1" over unnamed series has the authority of a printed key and says nothing.

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
