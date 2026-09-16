# PDF ingestion review — findings still open

This records what an independent review of the `ma/pdf-ingestion-phase-0` work found and did **not**
fix. Everything listed here survived an adversarial pass: a reviewer claimed it, and independent
skeptics whose instruction was to refute it read the code and could not. Each entry names the file
and the line, so none of it has to be rediscovered.

The defects that were fixed are described in the changelog rather than here.

## How this was produced

Reviewers were fanned across eight layers of the change — the PDF reader and text normalizer, table
and vector-chart extraction, the ingestion processors, the structure analyzer, the knowledge store
and ingestion service, index schema and retrieval, the indexer and connectors, and chat-time figure
exposure. Every finding was then put to two independent verifiers with different lenses, each told to
default to "refuted" when uncertain. Only unanimous survivors are recorded.

## Open findings

### Charts

**A figure's sloped segments all become one unnamed series, sorted by x.**
`src/Primitives/CrestApps.Core.AI.Documents.Pdf/Services/VectorPathChartDataExtractor.cs:195`

`BuildSeries` takes every segment steeper than the threshold anywhere in the figure cluster, pushes
both endpoints through the axis transforms, and returns a single series with no name and its points
re-sorted by x. A chart plotting two lines, or one line with diamond markers, yields one series
interleaving both lines and the marker edges. The values are real, so nothing marks them uncertain;
they are simply attributed to a series that does not exist. Splitting by polyline identity, and
naming series from the legend, is the work.

### Captions

**The in-text-reference fallback writes a per-page index into the document-wide figure number.**
`src/Primitives/CrestApps.Core.AI.Documents/Ingestion/Processors/FigureCaptionProcessor.cs:272`

`ApplyFallbacks` reads the image's position on its page and writes it to the ordinal that means "the
number printed on this figure". On page twelve, the only image has page-ordinal 1, so it matches a
sentence on page one that says "Figure 1 shows the overall architecture" and adopts it as context.
The two keys mean different things and the fallback conflates them.

### Structure

**A heading binds to the first contents entry under the threshold, not the closest.**
`src/Primitives/CrestApps.Core.AI.Documents/Knowledge/Structure/TocSeededStructureAnalyzer.cs:344`

`Distance` returns zero whenever one folded title is a prefix of the other and the shorter is at
least eight characters and at least half the longer. A themed issue listing both "Climate Change" and
"Climate Change and the Courts" therefore scores the second heading as a perfect match for the first
entry, and the seeds are consumed in listing order rather than by best fit. Scoring all seeds and
taking the minimum, and requiring a closer match before accepting a prefix, is the work.

### Re-ingestion

**An object that becomes excluded on re-ingest is never taken out of the index.**
`src/Primitives/CrestApps.Core.AI.Documents/Knowledge/DefaultKnowledgeIngestionService.cs:260`

Excluded objects are deliberately left out of the sync queue, and the superseded-removal pass
compares against the set of current canonical ids — which still contains them. So an article that was
indexed as ordinary content and is reclassified as an advertisement on a later ingest keeps its old
rows and stays answerable. The removal pass has to treat "still produced but now excluded" as gone.

**Re-ingesting identical bytes inside one uncommitted unit of work duplicates every object.**
`src/Primitives/CrestApps.Core.AI.Documents/Knowledge/DefaultKnowledgeIngestionService.cs:166`

Replacing what a file produced last time depends on reading the earlier set back, and inside a single
uncommitted session that read does not see writes from earlier in the same session. Uploading two
byte-identical files in one submit therefore stores every object twice under the same canonical id.

### Retrieval

**A content-type search against an index predating the typed columns fails closed.**
`src/Primitives/CrestApps.Core.AI/Services/DataSourceRetrieval.cs:269`

The filter names the real column, and the PostgreSQL and Azure translators now route that name to a
real column rather than into the filter bag. Against an index built before those columns existed the
provider errors, so the whole search fails rather than returning the rows it has. The tool advertises
the parameter to the model unconditionally, so the model reaches this on its own.

**A caller's filter-bag field sharing a reserved name is silently hijacked.**
`src/Primitives/CrestApps.Core.Infrastructure/DataSourceConstants.cs:76`

`IsTypedColumn` claims the unqualified names `contentType`, `rootId`, `parentId` and `page` for every
data source, so an existing data source whose own documents carry a top-level `contentType` now
filters against the knowledge column instead of its own field, and matches nothing. Scoping the
reserved names to knowledge-base indexes is the work.

### Performance

**A figure's duplicate lookup materializes every figure in the installation.**
`src/Stores/CrestApps.Core.Data.EntityCore/Services/EntityCoreKnowledgeObjectStore.cs:116`

`FindFigureByContentHashAsync` filters in memory across all data sources, once per pending figure. A
corpus of two hundred ingested magazines makes every backfill tick stream and deserialize hundreds of
thousands of rows. The content hash needs to be a denormalized column the query can filter on.

### Chat

**A data source search tool instance attached to an AI profile is never invoked.**
`src/Primitives/CrestApps.Core.AI/Tooling/Instances/DataSources/DataSourceSearchToolFunction.cs`

This is the open defect behind "the model will not show me a picture", and it is larger than the
pictures.

Reproduced against the real magazine through the MVC host. A profile with a `data-source-search`
tool instance attached, a matching system message and a capable chat deployment answers questions
about the magazine **entirely from invention**: a plausible Hungarian caption, a page number that
changes on every retry (8, then 12, then 15), and an image URL on `example.com` or
`cdn.magazine.example`. Entry logging placed at the very top of
`DataSourceRetrieval.SearchDetailedAsync` never fires, so the tool is not being called at all.

The same data source answers correctly when it is attached to a **chat interaction** instead, where
the system `search_data_sources` tool runs and returns the real article titles. So retrieval, the
index and the data itself are all sound; what fails is the profile-bound tool instance reaching the
model.

Until that is fixed, a figure link cannot be observed end to end in a chat, because no tool result
reaches the model to carry one.

Four defensive fixes were made along this chain while diagnosing it. Each is sound and two carry
tests, but none of them changed the observed behaviour, because the tool was never running:

- a figure is recognised from its canonical identifier when the index does not return the typed
  `contentType` column;
- a row with no `referenceType` falls back to the data source's own source type when choosing a link
  resolver;
- the figure link is now an absolute URL on the host serving the request rather than a bare path,
  since a model shown a path with no scheme reliably replaces it with one it invents;
- when no figure in a result set has a servable address, the result says so and tells the model never
  to invent a URL.

**Figures on documents attached to an AI profile can never be served.**
`src/Primitives/CrestApps.Core.AI.Documents/Endpoints/AIDocumentDownloadAuthorization.cs:83`

The switch handles chat interactions and chat sessions; everything else falls to `NotFound`. Profile
documents are stored with their own reference type, so both the document and the figure endpoints
always refuse them, while the figure tool still hands the model a link and tells it to embed the
picture. This fails closed, so it is a gap rather than a hole, but fixing it means deciding who may
read a profile's documents — there is no authorization handler over `AIProfile` today, and that is a
policy decision rather than a bug fix.

## What a real magazine did

The pipeline was run end to end against a 23-page trade magazine through the MVC host: create an
`Ingested` data source, upload, wait for the background transcription.

| | |
| --- | --- |
| Objects stored | 170 |
| Text chunks | 109 |
| Figures and charts | 43, every one captioned |
| Tables | 16 |
| Figures transcribed by the vision model | 25, averaging 716 characters |
| Figures kept caption-only by the salience budget | 18 |
| Articles | 14, three of them advertisement pages excluded from the index |

Every stored picture was a valid image, and the authorized figure endpoint served one back as a real
JPEG. That is the whole chain: read, figure extraction, caption resolution, salience tiering,
storage, background vision transcription, and authorized delivery.

**Article splitting needed a fix to work on that file.** Its page five holds 27 lines matching the
contents-entry pattern, but they point at pages 675, 780 and 903 — a parts or price listing, not a
contents page. The magazine has no contents page this approach can read, in either the "title … 12"
or the "12 … title" layout, so the whole issue was stored as one article and every question about
one of its pieces would have retrieved a blend of all of them.

The fix splits on the document's own headings when no contents page yields boundaries. A page opens
an article when its topmost element is set at least half again the body size and sits in the top band
of the page, and three such pages are required before the split is trusted. On this magazine that
turns one article into fourteen, with contiguous page ranges covering all 23 pages.

A bound rejecting contents entries that point past the last page was written and then reverted. A
magazine issue numbered from 100 lists folios well beyond its own extracted page count, so the bound
discards legitimate entries, and the heading stage already contains the false ones.

## Also worth knowing

Two rows of the sample-host source-parity suite
(`tests/CrestApps.Core.Tests.Samples/Tests/SourceParityTests.cs`) were already failing before this
branch, on screens it never touched: the chat-interaction create page disagrees on one heading, and
the settings page is missing three realtime fields in the Blazor host. That suite runs nightly rather
than on pull requests, so it does not gate a merge.
