---
sidebar_label: File Sources
sidebar_position: 7
title: File Sources
description: Feed a File AI data source from the host's file system, an FTP server or an SFTP server, and store what each file contains as separately retrievable text, figure, chart and table objects.
---

# File Sources

> Populate an AI knowledge base from files. A **File** data source stores what a file contains as separate
> objects — chunks of text, figures, charts and tables — so a question about a chart returns the chart rather
> than the page it happened to sit on. Where the files come from is a separate record that points at it.

## Two records, not one

A **File** data source is an inert target bucket. It holds no folder, no server and no credential, and no
field mapping either, because everything that lands in it already carries its own key, title and content.

A **file source** is the record that fills it. It chooses a **connector** — the host's file system, an FTP server or
an SFTP server — configures that connector, and selects the File data source it feeds. Several file sources
can feed one knowledge base, each on its own schedule.

That is exactly the shape a [web crawler](./web-crawlers.md) and a `Web` data source already have, and the
two are deliberately the same: the data source is the thing an AI profile attaches to, and what fills it is
managed on its own.

```text
File Source (file system)  ─┐
File Source (FTP)          ─┼──▶  File AI Data Source  ──▶  Knowledge-base index (typed objects)
File Source (SFTP)         ─┘
```

A file source and a web crawler are separate kinds of record, kept in separate stores, on separate screens.
What they share is the connector contract and the pipeline behind it, so a folder, a file server and a
website reach the same reader, the same enrichment and the same knowledge base.

## Creating one

Create a data source under **Data Sources** and choose the **File** source type. Select a knowledge-base
index profile as usual; there is nothing else to fill in, and the field mapping is hidden because there is
nothing for it to map.

Then open **File Sources** — the **File sources** button beside a File data source on the Data Sources
screen goes straight there — and add a record for each place the files come from. A record naming a data
source of another kind, or one that no longer exists, is refused when it is saved rather than at its first
run.

## Why a page is not one thing

Indexing a document by splitting its text into chunks averages everything on a page into one embedding. A
page carrying five hundred words, a chart and a table is four pieces of knowledge, not one. A `File` data
source stores them as four rows, each with a type, a page number and a link back to the article and document
it came from.

```text
document:{fileKey}
└── article:{fileKey}:1
    ├── text:{fileKey}:1:0      one chunk of body text
    ├── text:{fileKey}:1:1
    ├── figure:{fileKey}:1:0    caption + surrounding context + transcription
    ├── chart:{fileKey}:1:1     a figure whose caption says it is a chart
    └── table:{fileKey}:1:0     caption, columns and every cell labelled by its column
```

`fileKey` comes from the file's own bytes, so reading an identical file again replaces what it produced last
time instead of storing a second copy of it.

## Connectors

| Connector | Package | Reads |
| --- | --- | --- |
| `FileSystem` | `CrestApps.Core.AI.FileSources` | files in a folder on the host |
| `Ftp` | `CrestApps.Core.AI.Ftp` | files on an FTP or FTPS server |
| `Sftp` | `CrestApps.Core.AI.Sftp` | files on an SFTP server |

```csharp
builder.Services
    .AddCoreFileSources(builder.Configuration)
    .AddCoreFileSystemConnector()
    .AddCoreFtpIngestionConnector()
    .AddCoreSftpIngestionConnector();
```

Only the connectors a host registers are offered, so a host that never adds the file-transfer package offers
a folder and nothing else. See [File Source Connectors](./file-source-connectors.md) for the connector contract and for adding one.

## Reading a folder on the host

A file-system source stores `{ RootPath, Recursive, MaxItems }`. The folder it reads has to
sit inside a folder the **host** allows:

```json
{
  "CrestApps": {
    "AI": {
      "FileSources": {
        "FileSystem": {
          "AllowedRoots": [ "App_Data/file-sources" ]
        }
      }
    }
  }
}
```

That is the whole of what the host decides. Which folder inside the boundary to read, and whether to
recurse, belong to the file source, which cannot widen the boundary whatever it asks for.

A path is absolute, or relative to the **content root** — never to the process's current directory, which is
not the content root under IIS, a Windows service, or `dotnet run` from another folder.

:::warning
**The file-system connector reads nothing until a root is allowed.** Registering the connector grants
nothing: until `CrestApps:AI:FileSources:FileSystem:AllowedRoots` names a folder, every root is refused and
the file source fails validation before it lists a single file. The empty default is the point, not an
oversight — without it, an administrator with access to this screen could read any file the host process can
open.
:::

Both sample hosts allow `App_Data/file-sources` and create it at startup, so dropping files there is enough
to try a file source out. Nothing above it is reachable.

To allow a folder elsewhere for local development, name it in user secrets rather than in a committed file —
a path from your own machine belongs in neither this repository nor any configuration file that is checked
in:

```bash
cd src/Startup/CrestApps.Core.Mvc.Web
dotnet user-secrets set "CrestApps:AI:FileSources:FileSystem:AllowedRoots:0" "D:\your-folder"
```

Both hosts already declare a `UserSecretsId`, so there is nothing to initialize first. The array is indexed,
so a second root is `:1`, and an environment variable spells the same key
`CrestApps__AI__FileSources__FileSystem__AllowedRoots__0`.

### Staying inside the boundary

Three things are checked, and each refuses on its own:

- **A `..` is refused outright**, in a configured folder and in an item identifier alike, before any path is
  combined. Checking containment alone would accept `App_Data/file-sources/../../secrets` whenever it
  happened to land back inside a root, and a path that climbs out and returns is never what someone meant to
  type.
- **Containment is compared by path segment**, not by string prefix, so `App_Data/file-sources` does not
  admit `App_Data/file-sources-private`, and a folder legitimately named `..archive` is not mistaken for an
  escape.
- **An item identifier is re-checked when it is fetched**, not only when it is listed, because identifiers
  also arrive from stored state.

The refusal says which of these applies, since a bare "not allowed" sends an administrator to the
allowed-roots list when the real problem was a `..` they typed.

`Recursive` is measured from the file source's own folder, never from the allowed root above it: a source
pointed at `App_Data/file-sources/test` reads `test` and, when recursive, everything under `test` — and when
not recursive, only the files sitting directly in `test`.

Every file is listed, whatever its extension. Which of them can be read is the
[reader resolver](./file-source-connectors.md#reader-resolution)'s business, so narrowing intake is a matter of which
readers a host registers rather than a glob typed into a form.

## Reading a file server

An FTP or SFTP source stores a folder the same way — `{ RootPath, Recursive, MaxItems }` — and a connection
beside it: host, port, username and password, plus the encryption mode, data connection type and certificate
posture for FTP, and a private key and passphrase for SFTP.

A secret is **write-only in the UI**. The form is told that one is stored, never what it is, and a field left
blank keeps what is already there — so changing a port does not silently clear a password. Secrets are
encrypted with the same data protector the [MCP resource types](../mcp/resource-types.md) use, which is the
same one the connectors decrypt with; a mismatch there would read back as no credential at all rather than
as an error.

Changing a record from one protocol to the other takes the previous connection with it. A record never keeps
a credential for a server it no longer reads.

Accepting any TLS certificate is offered for FTP because self-signed certificates on internal file servers
are ordinary, but it turns validation off for that record. Use it only against a server you control.

## Running one

Two actions sit beside each file source:

- **Sync** runs it now, through `IFileSourceRunService`.
- **Reset state** forgets what has been read — not what was stored — so the next run re-reads every item.

A hosted job runs the sources that are due on their own schedule, and each run stores an
`FileSourceRunSummary` on the record: status, timings, how many items were seen, indexed, left unchanged,
removed and failed, how many figures were stored and how many still await transcription, and whether the
listing was complete. Removals happen **only** when the listing was complete, so a dropped connection or a
source larger than one run will take on never deletes what it failed to see.

An item whose content changes produces a new document, because a document's identifier comes from its bytes.
The run removes the document the item produced before — unless another item of the same file source still
produces it, since the same file placed twice is one document by design.

[File Source Connectors](./file-source-connectors.md) covers the rest of the run machinery: change tokens, discovery cursors, reader
resolution, and the per-record model and figure settings.

## Typed columns and filtering

Every knowledge-base row carries four typed columns alongside the existing ones:

| Column | Type | Holds |
| --- | --- | --- |
| `contentType` | keyword | `document`, `article`, `text`, `figure`, `chart` or `table` |
| `rootId` | keyword | the document the row belongs to |
| `parentId` | keyword | the article the row hangs off |
| `page` | integer | the page the row came from |

Rows written by every other source type carry `contentType = "text"` explicitly. Rows written before these
columns existed have no value, and **every reader treats a missing `contentType` as `text`**, so an index
built earlier keeps working untouched.

An index that already exists is offered the current schema on the next synchronization. PostgreSQL,
Elasticsearch and Azure AI Search all add the missing columns in place. A provider that cannot add fields
keeps serving the index it has: retrieval still works, it just cannot filter by type until the index is
recreated.

## Figures

A figure is kept for what can be said about it — its caption, the sentences around it, and, once a vision
model has transcribed it, its description. The picture itself is stored in the document file store and
served from `/ai/knowledge/{dataSourceId}/figures/{canonicalId}`, which is what a citation on a figure
answer links to. The endpoint authorizes the request against the owning data source with the
`AIKnowledgeOperations.ViewFigures` requirement, so a host decides who may see it by registering an
`AuthorizationHandler<OperationAuthorizationRequirement, AIDataSource>`.

Transcription never blocks a run. A figure worth describing that has not been described yet is searchable by
its caption immediately and is marked `pending-description`; the text of the document is indexed without
waiting for any picture.

## Articles within a document

A file is not always one piece of writing. A magazine is thirty, each with its own title, its own author and
its own subject, and indexing them as one document answers every question with a blend of all of them.

`TocSeededStructureAnalyzer` uses the document's own table of contents as the answer key: the listed titles
are the articles, and the only remaining question is which page prints which title — a fuzzy string match
rather than a judgement call. It also reads the page number printed on each page, which is rarely the page's
position in the file and is what a citation should name.

A page carrying neither a matched title nor a recurring running head is an advertisement. Its objects are
stored so the document stays complete and marked `Excluded`, so they are never indexed and can never be
returned as an answer. Front matter — the cover, the contents, the masthead — is never treated this way.

That rule only runs on a document that labels its pages consistently: at least 60% of the pages inside
articles have to carry a running head before the absence of one on a page is read as evidence of anything.
A magazine prints one on every editorial page, so a page without one is an advertisement. A book that prints
one on its chapter openers and nowhere else is saying nothing by omitting it, and reading that omission the
same way would exclude most of the book from the index.

A contents page is the answer key, not a precondition. Plenty of publications have none that can be read:
the page is laid out so no title and page number ever share a line, or what looks like a contents page is a
parts list. When no contents page yields boundaries, the document is split on its own headings instead — a
page opens a new article when its topmost element is set at least half again the body size and sits in the
top band of the page, so a subheading or a pull quote part way down a page cannot cut an article in two.
That needs at least three such pages before it is trusted.

Every step degrades to the one before it. No table of contents, no headings big enough to be titles, or
anything at all going wrong, and the document is one article, which is exactly what it was before any of
this existed.

Running heads are read in any script: a Greek, Cyrillic or Hungarian label is recognized on the same rule as
a Latin one.

`IPublicationMetadataExtractor` spends one utility-model call per ingest reading the front matter for what
the document says about itself — publication title, publisher, issue, date, ISSN — and stores it on the
document object. Nothing found simply means a citation names the file, as it did before.

## Retrieval

A search over a file knowledge base renders the figures and tables among its hits in their own blocks,
after the content and before the citations:

```text
Figures:
[fig:1] Figure 1. The measurements. (p. 8)   values: descriptive - not machine-readable
Tables:
[tbl:1] Table 2. Mechanical properties. - columns: Material, Strength (p. 5)
```

`[fig:1]` is a label, not an address. A model shows a figure by writing that label where the picture belongs,
and the host substitutes the picture for it, so a figure's address never passes through the model at all —
which is the point, because a model asked to copy a long opaque identifier writes one of the same shape with
different digits and cites a picture that was never in the results. The contract, both halves of it, is
written down under [Figures from a Knowledge Base](../core/chat.md#figures-from-a-knowledge-base).

The address the label stands for is resolved through the same `IAIReferenceLinkResolver` a citation uses and
carried on the figure as `RetrievedFigure.Link`. When the host registers no such resolver there is no address
to serve the picture from: the figure keeps its caption, its page and its logical
`crestapps://datasource/{dataSourceId}/figure/{canonicalId}` address (`RetrievedFigure.Uri`), which is what an
MCP client reads the picture through in any case, and nothing registers it as an image — an answer describes
such a figure rather than pointing at a picture the host cannot produce.

A chart whose values were only described says so outright, because a number read off a picture by eye looks
exactly like one lifted from the file's own geometry and only one of them is true.

A chart is told from a figure by its caption or description. The default keywords cover the common European
spellings — `chart`, `graph`, `diagram`, `grafikon`, `gráfico`, `graphique`, `wykres`, `график` and so on —
and `KnowledgeIngestionOptions.ChartKeywords` replaces them for a corpus in another language.

Hits are grouped by document and then by article within it, so a figure and the paragraph that cites it are
read as related rather than as two unconnected results. A figure is cited under the document it came from and
the page it was printed on, not under its own caption.

A data source search tool instance can be limited to certain kinds of knowledge with its **Content types**
setting (`DataSourceSearchToolSettings.ContentTypes`) — for example `figure, chart` for an instance that only
answers questions about pictures. Asking for `text` also matches rows written before the column existed.

A caller that wants the figures and tables as objects rather than as prose can use
`DataSourceRetrieval.SearchDetailedAsync`, which returns a `DataSourceRetrievalResult` carrying `Text`,
`Figures` and `Tables`.

## Backfilling transcriptions

`IFigureDescriptionBackfillService` transcribes the figures ingestion left pending. A hosted job drives it
every `KnowledgeIngestionOptions.BackfillIntervalSeconds` (default 30), taking `BackfillBatchSize` figures
per data source and transcribing `MaxConcurrentVisionCalls` of them at once. A host that prefers its own
scheduling can call the service directly.

A figure is transcribed by the model its own file source was configured with. A source that names no vision
deployment uses the host's own, and a configured deployment that cannot accept an image falls back to the
host's rather than silently transcribing nothing.

A figure whose transcription fails is marked `Failed` with the error recorded on it and is never retried
automatically: retrying a picture a model cannot read spends money on the same answer. An identical picture
already transcribed under the current prompt is copied rather than paid for again.

That applies to an answer from the model, not to a failure on the way to it. A dropped connection or a
timeout says nothing about whether the picture is readable, so a figure whose attempt never reached the
model is left pending and tried again on the next pass. Retiring it would lose it permanently over a fault
that had nothing to do with it.

`MaxConcurrentVisionCalls` only takes effect where the host registers a scope factory, which is everywhere
the hosted job runs. Each concurrent call takes its own scope, and therefore its own database context: two
calls sharing one would collide inside whatever the image analysis service depends on.

## Registration

`File` data sources come with document processing:

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIDocumentProcessing();
```

A host that only reads files into a knowledge base takes the ingestion path instead, and none of the chat,
tabular or tool services come with it:

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIDocumentIngestion(ingestion => ingestion
        .AddPlainTextReader()
        .AddFigureProcessing()
        .AddFigureBackfill()
    );
```

`AddCoreFileSources()` already does exactly that, so a file source host needs neither call. The **Files**
entry in the data source picker is registered by `AddCoreFileSources()`, because that is the package whose
screens configure it.

Add the figure download endpoint and the citation links that point at it:

```csharp
builder.Services.AddCoreAIDocumentReferenceDownloads();

// ...

app.AddDownloadKnowledgeFigureEndpoint();
```

Knowledge objects are stored through `IKnowledgeObjectStore`, implemented by both the Entity Framework Core
and YesSql store packages.

## Ingesting from code

`IKnowledgeIngestionService` is the one entry point, and it is what a file source's run calls for each item
it fetched:

```csharp
var result = await ingestionService.IngestAsync(
    dataSource,
    stream,
    fileName,
    mediaType,
    new KnowledgeIngestionOptions(),
    cancellationToken);
```

It returns the root identifier along with how many objects were produced, how many of them are figures, and
how many are still owed a transcription. `RemoveAsync(dataSource, rootId, cancellationToken)` takes a
document's objects, its stored figures and its index rows with it.

Ingesting the same bytes again replaces what they produced last time. Identical bytes produce identical
identifiers but not necessarily the same set of them — a reader or a processor that improved between two
ingests can split the text into fewer chunks, drop a figure it now scores as decoration, or reclassify a
figure as a chart — so whatever the previous ingest produced that this one did not is taken out of the index
and off the disk as part of the same call.

Ingesting a revised file produces a new document under a new root, because the root comes from the bytes.
A file source removes the document its item produced before; a host calling the service directly decides for
itself when the earlier version should go, with `RemoveAsync`.
