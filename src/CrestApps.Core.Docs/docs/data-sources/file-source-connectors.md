---
sidebar_label: File Source Connectors
sidebar_position: 8
title: File Source Connectors
description: Keep a File data source in step with a folder, an FTP server or an SFTP server through pluggable ingestion connectors.
---

# File Source Connectors

> Continuous intake. A **file source** points a connector at a folder or a server, and keeps a
> [File data source](./file.md) in step with it: what is new is read, what changed is re-read, and
> what is gone is removed.

This is the machinery behind a **file source**. [File Sources](./file.md) is the page to start from if you
are configuring one; this page is the contract each layer of it exposes.

## How it fits together

```text
  WHERE                      WHAT                       MEANING                      SHAPE
IIngestionConnector  ──▶  IngestionDocumentReader  ──▶  ingestion processors  ──▶  knowledge objects
  Sitemap (web)             Pdf                         figure caption               document / article
  FileSystem                OpenXml                     figure salience              text / figure / chart / table
  Ftp / Sftp                PlainText                   figure description
```

Each layer is independently pluggable: a new source is one class and one registration, a new file type is one
reader and one registration, and neither touches retrieval.

## Records and stores

A file source and a web crawler are different things, so they are different records in different stores:

| Record | Store | Configures |
| --- | --- | --- |
| `FileSource` | `IFileSourceStore` | a folder or a file server, read by an ingestion connector |
| `WebCrawler` | `IWebCrawlerStore` | a website, read by a crawl strategy |

Both derive from `IngestionSource`, which is the shape everything downstream works against: a `Source`
naming what reads it, the data source it fills, whether it is enabled, how often it runs, and its own
settings. That is what a connector is handed, and it is the only thing the two kinds share.

Per-item state is separated the same way. A run records what it read in `IIngestionItemStateStore` as
`IngestionItemState` — an item key, the connector's opaque change token, and the document the item produced.
`IWebCrawlStateStore` is the crawl-specific state the re-index service keeps for a crawler feeding a `Web`
data source.

Register the stores for the backend the host uses:

```csharp
builder.Services.AddCoreFileSourceStoresYesSql();      // YesSql
// builder.Services.AddCoreFileSourceStoresEntityCore(); // EntityFramework Core
```

## Connectors

A connector answers two questions and nothing else — what is there, and give me that one:

```csharp
public interface IIngestionConnector
{
    string Name { get; }
    ValueTask ValidateAsync(IngestionSource settings, ValidationResultDetails result, CancellationToken ct = default);
    Task<IngestionDiscoveryResult> DiscoverAsync(IngestionSource settings, string continuationToken = null, CancellationToken ct = default);
    Task<IngestionItemContent> FetchAsync(IngestionSource settings, string itemId, CancellationToken ct = default);
}
```

It is handed an `IngestionSource` — the shape a configured source has, whichever kind it is — rather than one
particular record type, so the same connector reads for a `FileSource` and for a `WebCrawler` without knowing
which it was given.

| Connector | Package | Reads |
| --- | --- | --- |
| `Sitemap` | `CrestApps.Core.AI.WebCrawlers` | pages discovered through a site's sitemap |
| `FileSystem` | `CrestApps.Core.AI.FileSources` | files in a folder on the host |
| `Ftp` | `CrestApps.Core.AI.Ftp` | files on an FTP or FTPS server |
| `Sftp` | `CrestApps.Core.AI.Sftp` | files on an SFTP server |

```csharp
builder.Services
    .AddCoreFileSources()
    .AddCoreFileSystemConnector()
    .AddCoreFtpIngestionConnector()
    .AddCoreSftpIngestionConnector();

builder.Services.AddCoreFileSources(builder.Configuration);
```

## A partial listing never deletes anything

`IngestionDiscoveryResult.IsComplete` is the most important value in the intake path. Removals happen **only**
when it is `true`.

A file server that drops a connection mid-listing, a folder with more files than one run will take on, a
paged listing whose second page failed — each returns what it has and says the listing is incomplete. Treated
as complete, any of them would delete every item it failed to see, and nothing downstream could tell that
apart from a genuine deletion.

## Reading a folder on the host

A file-system source stores `{ RootPath, Recursive, MaxItems }`, and the folder it names has to sit inside a
folder the host allows:

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

Empty means no folder may be read at all, which is the default. Without it, an administrator with access to
the File Sources screen can read any file the host process can open. A `..` is refused outright — in a
configured folder and in an item identifier alike — and an item identifier is re-checked when it is fetched
as well as when it is listed, because identifiers also arrive from stored state.

Registering the connector grants nothing on its own.
[File Sources](./file.md#reading-a-folder-on-the-host) has the whole rule, what the sample hosts allow, and
how to name a root for local development without committing it.

## Reading a file server

An FTP or SFTP source stores a folder the same way — `{ RootPath, Recursive, MaxItems }` — and a
connection beside it: host, port, username and password, plus the encryption mode, data connection
type and certificate posture for FTP, and a private key and passphrase for SFTP. Both are edited on
the record's own screen in both sample hosts.

A secret is **write-only in the UI**. The form is told that one is stored, never what it is, and a
field left blank keeps what is already there — so changing a port does not silently clear a
password. Secrets are encrypted with the same data protector the
[MCP resource types](../mcp/resource-types.md) use, which is the same one the connectors decrypt
with; a mismatch there would read back as no credential at all rather than as an error.

Changing a record from one protocol to the other takes the previous connection with it. A record
never keeps a credential for a server it no longer reads.

Accepting any TLS certificate is offered for FTP because self-signed certificates on internal file
servers are ordinary, but it turns validation off for that record. Use it only against a server you
control.

## What counts as changed

Every listed item carries an opaque **change token**, compared verbatim and never parsed: an `ETag` for one
source, a modified time and size for another. An item whose token is unchanged is not re-read. An item with
**no** token is re-read every run — FTP in particular often reports neither a time nor a size, and "no
evidence of change" is not evidence of no change.

## Per-record ingestion settings

Figure transcription is the expensive part of ingestion, and how much of it is worth paying for depends on
the corpus. `FileSourceMetadata` carries `FigureMode`, `VisionDeploymentName`, `UtilityDeploymentName`,
`EmbeddingDeploymentName`, `MaxFigureDescriptionsPerDocument`, `MaxItemsPerRun` and `Language`, so a folder
of scanned datasheets and a folder of meeting minutes can sit side by side under different settings. They are
edited on the record's own screen in both sample hosts.

Leaving a deployment unset means "use the application's", never "disable" - `FigureMode = Off` is how figure
transcription is turned off. A deployment that cannot do the job it was chosen for is **refused when it is
saved**: one that does not accept image input cannot be the vision deployment, and one that produces no
embeddings cannot be the embedding deployment. A deployment that is later deleted does not fail a run; the
application's own is used and the run carries on.

## What a run records

`IFileSourceRunService.RunAsync` returns an `FileSourceRunSummary` and stores it on the record, so the last run is
visible without reading a log: status, when it started and finished, how many items were seen, indexed, left
unchanged, removed and failed, how many figures were stored and how many still await transcription, and
whether the listing was complete.

The status is `Running` while the run is in progress, then `Succeeded`, `PartiallyCompleted` (some items
failed, or the listing covered only part of the source) or `Failed` (it could not start, or discovery
failed).

Two actions sit beside each record:

- **Sync** runs it now. The data source decides the path: a record feeding a **File** data source is a file
  source and runs through `IFileSourceRunService`; one feeding a **Web** data source is a web crawler and runs
  through the re-index planner.
- **Reset state** forgets what has been read - not what was stored - so the next run re-reads every item.

An item whose content changes produces a new document, because a document's identifier comes from its bytes.
The run removes the document the item produced before — unless another item of the same record still
produces it, since the same file placed twice is one document by design.

## Working through a large source

A run reads at most `MaxItemsPerRun` items. When the source holds more, the connector reports the listing as
incomplete and returns a **discovery cursor**; the next run resumes from it. Because a resumed run has seen
only a window onto the source, it is never complete and **never removes anything** - deciding something is
gone needs a listing of the whole source in one pass.

Fetching runs `MaxConcurrentFetches` items ahead of ingestion. Fetching waits on a network and is worth
overlapping; ingestion writes to stores that are not safe to use from two threads at once, so it stays
single-threaded.

## Reader resolution

An extension is a claim, not a fact. `IIngestionDocumentReaderResolver` asks, in order:

1. the media type the connector declared, when it is specific;
2. the first bytes — `%PDF-`, PNG, JPEG, or a zip container disambiguated by extension;
3. the extension, which is how every reader was resolved before this existed.

The stream is always returned to where it was found.

`AddCoreFileSources()` also registers the HTML reader for `.html`, `.htm` and `text/html`. A connector hands over
whatever a folder or a server holds, and a web page read that way is HTML rather than the cleaned text a crawl
strategy produces; the plain-text reader the ingestion path registers for those keys would index the markup.
Keyed readers resolve to the last registration, so a host that also wants chat document processing calls
`AddDocumentProcessing()` **before** `AddCoreFileSources()`.

## Running

Two services, so a host can take either half:

| Service | Does |
| --- | --- |
| `IFileSourceRunService` | one run of one source, and returns what it did |
| `IFileSourceScheduler` | finds everything that is due and runs it |

`FileSourceBackgroundService` is a timer and nothing else: every
`FileSourceOptions.RunCheckIntervalMinutes` it opens a scope and calls `IFileSourceScheduler.RunDueAsync`. A
host that schedules its own work — Orchard Core's background tasks, a cron job, a queue worker, an operator
pressing a button — resolves the scheduler and calls it, and gets the same behaviour without taking the
hosted service or its timer with it:

```csharp
// Inside a scope the host already owns.
var scheduler = scope.ServiceProvider.GetRequiredService<IFileSourceScheduler>();
var result = await scheduler.RunDueAsync(cancellationToken);

// Or spread the work over the host's own workers.
var due = await scheduler.GetDueAsync(DateTimeOffset.UtcNow, cancellationToken);
```

The scheduler is **scoped and creates no scope of its own**, so the caller's scope — a shell scope, a
request, a unit of work — is the one the reads and writes happen in. A host that does its own scheduling
should leave `FileSourceBackgroundService` unregistered, or the same sources are driven twice.

Whether a source is due is read from the run summary stored on it, so the schedule survives a restart. One
source that throws is logged and skipped; the others still get their turn, and the result says how many were
considered, ran and failed.

Every file source is the scheduler's. A web crawler is its too, but only when it feeds a **File** data
source: one feeding a **Web** data source is driven by the re-index service instead, and running it here as
well would run it twice and overwrite the crawl state that service keeps.

Each background pass commits the transactional store once it finishes, the same way the indexing queue does,
so the knowledge objects, the per-item state and the run summary a run wrote are actually kept.

Web crawlers gained the same completeness rule. A sitemap walk that stopped short - the page limit was
reached, a child sitemap could not be downloaded, or the graph is nested deeper than the crawler follows -
now reports itself incomplete, and the planner removes nothing on the strength of it. The result's status is
`PartiallyDiscovered` and its message says which of those happened.
