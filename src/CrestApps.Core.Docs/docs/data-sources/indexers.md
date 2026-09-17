---
sidebar_label: Indexers
sidebar_position: 8
title: Indexers
description: Keep a File data source in step with a folder, an FTP server or an SFTP server through pluggable ingestion connectors.
---

# Indexers

> Continuous intake. An **indexer** points a connector at a source, and keeps a
> [File data source](./file.md) in step with it: what is new is read, what changed is re-read, and
> what is gone is removed.

This is the machinery behind a **file source**. [File Sources](./file.md) is the page to start from if you
are configuring one; this page is the contract each layer of it exposes.

## How it fits together

```text
  WHERE                      WHAT                       MEANING                      SHAPE
IIngestionConnector  ──▶  IngestionDocumentReader  ──▶  ingestion processors  ──▶  knowledge objects
  Sitemap (web)             Pdf                         figure caption               document / article
  LocalFolder               OpenXml                     figure salience              text / figure / chart / table
  Ftp / Sftp                PlainText                   figure description
```

Each layer is independently pluggable: a new source is one class and one registration, a new file type is one
reader and one registration, and neither touches retrieval.

## Connectors

A connector answers two questions and nothing else — what is there, and give me that one:

```csharp
public interface IIngestionConnector
{
    string Name { get; }
    ValueTask ValidateAsync(WebCrawler settings, ValidationResultDetails result, CancellationToken ct = default);
    Task<IngestionDiscoveryResult> DiscoverAsync(WebCrawler settings, string continuationToken = null, CancellationToken ct = default);
    Task<IngestionItemContent> FetchAsync(WebCrawler settings, string itemId, CancellationToken ct = default);
}
```

| Connector | Package | Reads |
| --- | --- | --- |
| `Sitemap` | `CrestApps.Core.AI.WebCrawlers` | pages discovered through a site's sitemap |
| `LocalFolder` | `CrestApps.Core.AI.Indexers` | files in a folder on the host |
| `Ftp` | `CrestApps.Core.AI.Ftp` | files on an FTP or FTPS server |
| `Sftp` | `CrestApps.Core.AI.Sftp` | files on an SFTP server |

```csharp
builder.Services
    .AddCoreIndexers()
    .AddCoreLocalFolderConnector()
    .AddCoreFtpIngestionConnector()
    .AddCoreSftpIngestionConnector();

builder.Services.Configure<IndexerOptions>(
    builder.Configuration.GetSection("CrestApps:Indexers"));
```

## A partial listing never deletes anything

`IngestionDiscoveryResult.IsComplete` is the most important value in the intake path. Removals happen **only**
when it is `true`.

A file server that drops a connection mid-listing, a folder with more files than one run will take on, a
paged listing whose second page failed — each returns what it has and says the listing is incomplete. Treated
as complete, any of them would delete every item it failed to see, and nothing downstream could tell that
apart from a genuine deletion.

## Reading a local folder

A local-folder source stores `{ RootPath, SearchPattern, Recursive, MaxItems }`, and the root has to sit
inside a host-allow-listed folder:

```json
{
  "CrestApps": {
    "Indexers": {
      "AllowedLocalRoots": [ "D:\\knowledge" ],
      "MaxItemsPerRun": 200
    }
  }
}
```

Empty means no local folder may be indexed at all, which is the default. Without it, an administrator with
access to the File Sources screen can read any file the host process can open. An item identifier that
resolves outside the root is refused when it is fetched as well as when it is listed, because identifiers
also arrive from stored state.

Registering the connector grants nothing, and neither sample host ships a root, so cloning the repository
grants nothing either. [File Sources](./file.md#reading-a-local-folder) has the whole rule and how to name a
root for local development without committing it.

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

Changing a record from one protocol to the other, or to a crawl strategy, takes the previous
connection with it. A record never keeps a credential for a server it no longer reads.

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
the corpus. `IndexerMetadata` carries `FigureMode`, `VisionDeploymentName`, `UtilityDeploymentName`,
`EmbeddingDeploymentName`, `MaxFigureDescriptionsPerDocument`, `MaxItemsPerRun` and `Language`, so a folder
of scanned datasheets and a folder of meeting minutes can sit side by side under different settings. They are
edited on the record's own screen in both sample hosts.

Leaving a deployment unset means "use the application's", never "disable" - `FigureMode = Off` is how figure
transcription is turned off. A deployment that cannot do the job it was chosen for is **refused when it is
saved**: one that does not accept image input cannot be the vision deployment, and one that produces no
embeddings cannot be the embedding deployment. A deployment that is later deleted does not fail a run; the
application's own is used and the run carries on.

## What a run records

`IIndexerRunService.RunAsync` returns an `IndexerRunSummary` and stores it on the record, so the last run is
visible without reading a log: status, when it started and finished, how many items were seen, indexed, left
unchanged, removed and failed, how many figures were stored and how many still await transcription, and
whether the listing was complete.

The status is `Running` while the run is in progress, then `Succeeded`, `PartiallyCompleted` (some items
failed, or the listing covered only part of the source) or `Failed` (it could not start, or discovery
failed).

Two actions sit beside each record:

- **Sync** runs it now. The data source decides the path: a record feeding a **File** data source is a file
  source and runs through `IIndexerRunService`; one feeding a **Web** data source is a web crawler and runs
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

`AddCoreIndexers()` also registers the HTML reader for `.html`, `.htm` and `text/html`. A connector hands over
whatever a folder or a server holds, and a web page read that way is HTML rather than the cleaned text a crawl
strategy produces; the plain-text reader document processing registers for those keys would index the markup.
Keyed readers resolve to the last registration, so call `AddCoreIndexers()` after `AddDocumentProcessing()`.

## Running

A hosted job runs the records that are due every `IndexerOptions.RunCheckIntervalMinutes`; a host that
prefers its own scheduling calls `IIndexerRunService` directly. Whether a record is due is read from the run
summary stored on it, so the schedule survives a restart.

Only records that feed a **File** data source run here. A crawler that feeds a **Web** data source is
driven by the web-crawler re-index service, and that service in turn leaves records feeding a File data
source alone — each record runs on exactly one path, decided by the kind of data source it fills. The crawler
catalog handler enforces the pairing when a record is saved: a connector that is not also a crawl strategy can
only feed a File data source, and a crawl strategy can feed either kind.

Each background pass commits the transactional store once it finishes, the same way the indexing queue does,
so the knowledge objects, the per-item state and the run summary a run wrote are actually kept.

Web crawlers gained the same completeness rule. A sitemap walk that stopped short - the page limit was
reached, a child sitemap could not be downloaded, or the graph is nested deeper than the crawler follows -
now reports itself incomplete, and the planner removes nothing on the strength of it. The result's status is
`PartiallyDiscovered` and its message says which of those happened.
