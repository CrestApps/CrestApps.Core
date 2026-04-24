---
sidebar_label: Document Processing
sidebar_position: 6
title: Document Processing
description: Document readers, semantic search, and tabular data extraction for RAG-powered chat experiences.
---

# Document Processing

> Reads, chunks, and indexes uploaded documents so the AI can search and reference them during conversations.

The document pipeline now lives in the dedicated `CrestApps.Core.AI.Documents` package. `CrestApps.Core.AI` and `CrestApps.Core.AI.Chat` remain focused on the core AI runtime and chat runtime.

## Quick Start

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddMarkdown()
        .AddChatInteractions()
        .AddDocumentProcessing(documentProcessing => documentProcessing
            .AddEntityCoreStores()
            .AddOpenXml()
            .AddPdf()
            .AddReferenceDownloads()
        )
        .AddOpenAI()
    )
    .AddEntityCoreSqliteDataStore("Data Source=app.db")
);

app.AddChatApiEndpoints()
    .AddDownloadAIDocumentEndpoint();
```

## Problem & Solution

Users upload documents (PDFs, Word files, spreadsheets) and expect the AI to answer questions about them. This requires:

- **Reading** diverse file formats into plain text
- **Chunking** large documents into embeddable segments
- **Embedding** chunks into vector space for semantic search
- **Searching** relevant chunks at query time (RAG)
- **Tabular processing** for CSV/Excel data with structured queries

The document processing system handles the full pipeline from upload to retrieval.

## Services Registered by `AddCoreAIDocumentProcessing()`

| Service | Implementation | Lifetime | Purpose |
|---------|---------------|----------|---------|
| `IAIDocumentProcessingService` | `DefaultAIDocumentProcessingService` | Scoped | Reads, chunks, and materializes `AIDocument` / `AIDocumentChunk` records |
| `ITabularBatchProcessor` | `TabularBatchProcessor` | Scoped | Processes CSV/Excel batch queries |
| `ITabularBatchResultCache` | `TabularBatchResultCache` | Singleton | Caches tabular query results |
| `DocumentOrchestrationHandler` | — | Scoped | Injects document context into orchestration |

`AddCoreAIDocumentProcessing()` and the `AddDocumentProcessing(...)` builder extension are provided by `CrestApps.Core.AI.Documents`.

### Citation download links

Attached-document citations are an opt-in document-processing feature made of two registrations:

1. `AddReferenceDownloads()` on `CrestAppsDocumentProcessingBuilder` (or `AddCoreAIDocumentReferenceDownloads()` on `IServiceCollection`) registers `DocumentAIReferenceLinkResolver` for `AIReferenceTypes.DataSource.Document`.
2. `AddDownloadAIDocumentEndpoint()` maps the shared download route that serves the cited file back to the browser.

Use both when you want `[doc:n]` references for attached AI documents to render as downloadable links in your chat UI:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddDocumentProcessing(documentProcessing => documentProcessing
            .AddEntityCoreStores()
            .AddOpenXml()
            .AddPdf()
            .AddReferenceDownloads()
        )
    )
);

app.AddChatApiEndpoints()
    .AddDownloadAIDocumentEndpoint();
```

If you prefer the raw service surface instead of the builder API:

```csharp
builder.Services.AddCoreAIDocumentProcessing();
builder.Services.AddCoreAIDocumentReferenceDownloads();

app.AddChatApiEndpoints()
    .AddDownloadAIDocumentEndpoint();
```

### Built-in Document Readers

`AddDocumentProcessing(...)` registers the plain-text and tabular readers. OpenXml and PDF readers now live in the dedicated `CrestApps.Core.AI.Documents.OpenXml` and `CrestApps.Core.AI.Documents.Pdf` packages, so hosts opt into those dependencies explicitly with the nested builder calls `AddOpenXml()` and `AddPdf()` or, if they prefer the raw `IServiceCollection` surface, `AddCoreAIOpenXmlDocumentProcessing()` and `AddCoreAIPdfDocumentProcessing()`. Markdown-aware normalization now also lives in its own `CrestApps.Core.AI.Markdown` package. `AddAISuite(...)` does not register it automatically, so hosts that want Markdig-backed normalization and chunking must opt in with `AddMarkdown()` or `AddCoreAIMarkdown()`.

| Reader | Supported Extensions | Embeddable |
|--------|---------------------|------------|
| `PlainTextIngestionDocumentReader` | `.txt`, `.md`, `.json`, `.xml`, `.html`, `.htm`, `.log`, `.yaml`, `.yml` | Yes |
| `PlainTextIngestionDocumentReader` | `.csv` | No (tabular) |
| `OpenXmlIngestionDocumentReader` | `.docx`, `.pptx` | Yes |
| `OpenXmlIngestionDocumentReader` | `.xlsx` | No (tabular) |
| `PdfIngestionDocumentReader` | `.pdf` | Yes |

### System Tools for Documents

These tools are automatically available to the orchestrator when documents are attached:

| Tool | Purpose |
|------|---------|
| `SearchDocumentsTool` | Semantic vector search across uploaded documents |
| `ReadDocumentTool` | Reads full text of a specific document |
| `ReadTabularDataTool` | Reads and parses CSV/TSV/Excel data |

For chat-interaction and chat-session uploads, the orchestration layer now chooses between chunked retrieval and full-document injection automatically. Lookup-style questions still use semantic search, while whole-document requests such as summaries, reviews, rewrites, translations, or complete extraction tasks preload the full uploaded file content into context.


## Key Interfaces

### `IAIDocumentProcessingService`

Processes an uploaded file after the host has resolved any embedding generator it wants to use.

```csharp
public interface IAIDocumentProcessingService
{
    Task<DocumentProcessingResult> ProcessFileAsync(
        IFormFile file,
        string referenceId,
        string referenceType,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator);
}
```

The framework no longer asks `IAIDocumentProcessingService` to create embedding generators. Hosts resolve the embedding deployment through `IAIDeploymentManager` and create the generator through `IAIClientFactory`, then pass it into `ProcessFileAsync(...)`. That keeps deployment selection and AI client creation in the shared client/deployment runtime instead of duplicating that logic inside the document processor.

### Adding a Custom Document Reader

Register a reader for additional file formats:

```csharp
builder.Services.AddCoreAIIngestionDocumentReader<MyCustomReader>(".custom", ".myformat");
```

Implement the reader:

```csharp
public sealed class MyCustomReader : IngestionDocumentReader
{
    public override Task<IngestionDocument> ReadAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        // Parse the stream into sections and elements
    }
}
```

## Configuration

### `ChatDocumentsOptions`

Controls which file types can be uploaded and processed.

```csharp
services.Configure<ChatDocumentsOptions>(options =>
{
    // Add a new embeddable extension
    options.Add(".rtf", embeddable: true);

    // Add a tabular (non-embeddable) extension
    options.Add(".tsv", embeddable: false);
});
```

- **AllowedFileExtensions** — Complete set of uploadable extensions
- **EmbeddableFileExtensions** — Subset that gets vector-embedded (non-embeddable files use direct read tools instead)

Use the registered option values to drive your upload UI as well as validation:

- **AI Profile / AI Template knowledge uploads** should use `EmbeddableFileExtensions`
- **Chat interaction / chat session uploads** should use `AllowedFileExtensions`

That keeps file pickers, visible supported-format text, and server-side processing aligned with the readers actually registered in the app.

### Limits

- Maximum **25,000 characters** total for embedding per session
- Results are cached via `IDistributedCache` for batch tabular queries

## Storage

Document metadata and chunks require store implementations. Register stores directly on the document processing builder:

**Entity Framework Core (via builder):**

```csharp
.AddDocumentProcessing(documentProcessing => documentProcessing
    .AddEntityCoreStores()
    .AddOpenXml()
    .AddPdf()
    .AddReferenceDownloads()
)
```

**YesSql (via builder):**

```csharp
.AddDocumentProcessing(documentProcessing => documentProcessing
    .AddYesSqlStores()
    .AddOpenXml()
    .AddPdf()
    .AddReferenceDownloads()
)
```

Both register `IAIDocumentStore`, `IAIDocumentChunkStore`, and `IAIDataSourceStore`. The `ISearchIndexProfileStore` is registered separately through the indexing services builder — see [Data Storage](data-storage.md) for the full per-feature store reference.

Uploaded document files are stored through `IDocumentFileStore`, which extends the general `IFileStore` abstraction:

```csharp
builder.Services.AddSingleton<IDocumentFileStore, AzureBlobDocumentFileStore>();
```

The MVC sample host stores uploads on the local file system. Each upload gets a new GUID-based stored file name to avoid collisions, while the original user-facing file name remains in `AIDocument.FileName`. The persisted document record also keeps the GUID-based stored file name/path (`StoredFileName` / `StoredFilePath`) so hosts can trace and delete the physical file later.

Replace `IDocumentFileStore` when you want uploaded profile, chat-interaction, or chat-session files to land in a different backend such as Azure Blob Storage instead of the local file system used by the MVC sample host.
