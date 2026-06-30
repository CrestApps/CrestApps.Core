---
sidebar_label: AI Documents
sidebar_position: 14
title: AI Documents
description: Upload, process, chunk, embed, and search documents so the AI model can retrieve relevant content during conversations (RAG).
---

# AI Documents

> A complete document management pipeline that reads uploaded files, splits them into chunks, generates vector embeddings, and makes the content searchable via semantic similarity — enabling retrieval-augmented generation (RAG) in AI conversations.

## Quick Start

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIOrchestration()
    .AddCoreAIChatInteractions()
    .AddCoreAIDocumentProcessing()
    .AddCoreAIOpenAI();

// Register document and chunk stores
builder.Services.AddScoped<IAIDocumentStore, YesSqlAIDocumentStore>();
builder.Services.AddScoped<IAIDocumentChunkStore, YesSqlAIDocumentChunkStore>();
```

`AddCoreAIDocumentProcessing()` is shipped by `CrestApps.Core.AI.Documents`.

Upload a file and process it:

```csharp
public sealed class DocumentUploadController(
    IAIDocumentProcessingService processingService,
    IAIClientFactory aiClientFactory,
    IAIDeploymentManager deploymentManager,
    IAIDocumentStore documentStore) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Upload(
        IFormFile file,
        string referenceId,
        string referenceType)
    {
        var embeddingDeployment =
            await deploymentManager.ResolveOrDefaultAsync(AIDeploymentPurpose.Embedding);
        var embeddingGenerator = embeddingDeployment is null
            ? null
            : await aiClientFactory.CreateEmbeddingGeneratorAsync(embeddingDeployment);

        var result = await processingService.ProcessFileAsync(
            file, referenceId, referenceType, embeddingGenerator);

        return result.Succeeded ? Ok(result) : BadRequest(result);
    }
}
```

## Problem & Solution

Users upload documents (PDFs, Word files, spreadsheets, text files) and expect the AI to answer questions about them. This requires a multi-stage pipeline:

- **Reading** — Extract plain text from diverse file formats (`.pdf`, `.docx`, `.xlsx`, `.csv`, `.txt`, `.md`, and more)
- **Chunking** — Split large documents into segments small enough to embed
- **Embedding** — Convert each chunk into a vector representation using a configured embedding model
- **Indexing** — Store embeddings in a vector search index (Elasticsearch or Azure AI Search)
- **Searching** — At query time, perform semantic similarity search to find the most relevant chunks
- **Tabular processing** — Non-embeddable files (such as CSV and Excel) are delegated to the system **Tabular Data Agent**, which loads them lazily into an in-memory SQLite database and queries them with SQL

The document processing system handles this full pipeline from upload to retrieval, while the built-in document tools make the content available to the AI during orchestration.

When a chat deployment also supports the `Vision` purpose, chat interaction and chat session uploads can include supported image formats (`.bmp`, `.gif`, `.jpeg`, `.jpg`, `.png`, `.webp`) alongside standard document files. Those images are stored as `AIDocument` records, analyzed at upload time by `IImageAnalysisService` to extract a structured summary (caption, OCR text, detected entities), and the results are persisted as `AIDocumentChunk` records — exactly like text documents. This makes image content available through the same `read_document` and `search_documents` tools used for regular documents.

For cases where the text analysis is insufficient (e.g., reading fine text, comparing visual elements, or understanding spatial layout), the `inspect_image` tool provides on-demand raw image inspection by sending the original bytes to a vision model in a one-shot call. This approach eliminates the cost of attaching raw image bytes to every chat request while preserving full visual inspection capability when needed.

The `ChatDocumentsOptions.AnalyzeImagesAtUpload` setting controls whether analysis runs at upload time, and `MaxInspectImageCallsPerRequest` limits how many costly raw-image inspections the model can perform per turn.

### Creating a chat client to describe an image

When you want to call a vision-capable model directly, resolve the deployment, create an `IChatClient`, and send a multimodal user message:

```csharp
public sealed class ImageDescriptionService(
    IAIDeploymentManager deploymentManager,
    IAIClientFactory clientFactory)
{
    public async Task<string> DescribeImageAsync(
        string imagePath,
        string chatDeploymentName,
        CancellationToken cancellationToken = default)
    {
        var deployment = await deploymentManager.ResolveOrDefaultAsync(
            AIDeploymentPurpose.Chat,
            deploymentName: chatDeploymentName,
            cancellationToken: cancellationToken);

        if (deployment?.Purpose.Supports(AIDeploymentPurpose.Vision) != true)
        {
            throw new InvalidOperationException("The selected chat deployment does not support vision.");
        }

        var chatClient = await clientFactory.CreateChatClientAsync(deployment);
        var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var mediaType = MediaTypeHelper.InferMediaType(Path.GetExtension(imagePath));

        var response = await chatClient.GetResponseAsync(
            [
                new ChatMessage(
                    ChatRole.User,
                    [
                        new TextContent("Describe this image in detail."),
                        new DataContent(imageBytes, mediaType),
                    ]),
            ],
            cancellationToken: cancellationToken);

        return response.Text;
    }
}
```

## Architecture Overview

```text
┌─────────────┐
│  User Upload │
└──────┬──────┘
       ▼
┌──────────────────────────────┐
│  IAIDocumentProcessingService │  ← Orchestrates the pipeline
├──────────────────────────────┤
│  1. Store document record     │  → IAIDocumentStore
│  2. Read file content         │  → IngestionDocumentReader (keyed by extension)
│  3. Normalize & chunk text    │  → RagTextNormalizer
│  4. Store chunks              │  → IAIDocumentChunkStore
│  5. Generate embeddings       │  → IEmbeddingGenerator<string, Embedding<float>>
│  6. Index in vector store     │  → ISearchDocumentManager (Elasticsearch / Azure AI)
└──────────────────────────────┘

       ┌─────────────────────────────────────┐
       │          During Conversation         │
       ├─────────────────────────────────────┤
       │  DocumentOrchestrationHandler        │
       │  detects documents on the session    │
       │  and injects document tools:         │
       │                                      │
       │  • SearchDocumentsTool (vector RAG)  │
       │  • ReadDocumentTool (full text read) │
       │  • InspectImageTool (vision on-demand)│
       └──────────────┬──────────────────────┘
                      ▼
       ┌─────────────────────────────────────┐
       │  AI Model calls tools as needed      │
       │  to answer user questions about      │
       │  the uploaded documents.             │
       │  Tabular files (e.g. CSV/Excel) are  │
       │  delegated to the always-available   │
       │  Tabular Data Agent, which queries,  │
       │  mutates, and exports them from an   │
       │  in-memory SQL workspace.            │
       └─────────────────────────────────────┘
```

## Core Interfaces

| Interface | Package | Purpose |
|-----------|---------|---------|
| `IAIDocumentStore` | `CrestApps.Core.AI.Documents` | CRUD for document records |
| `IAIDocumentChunkStore` | `CrestApps.Core.AI.Documents` | CRUD for document chunks |
| `IAIDocumentProcessingService` | `CrestApps.Core.AI.Documents` | Orchestrates file → chunk → embed → index |
| `IDocumentFileStore` | `CrestApps.Core.AI.Documents` | Persists uploaded document files to a swappable storage backend |
| `ISearchDocumentManager` | `CrestApps.Core.AI` | Manages documents in the vector search index |
| `IVectorSearchService` | `CrestApps.Core.AI` | Performs vector similarity search at query time |
| `ITabularBatchProcessor` | `CrestApps.Core.AI.Documents` | Splits and processes CSV/Excel batch queries |
| `ITabularBatchResultCache` | `CrestApps.Core.AI.Documents` | Caches tabular query results |
| `IngestionDocumentReader` | `CrestApps.Core.AI.Documents` | Abstract base for format-specific file readers |

`AddCoreAIDocumentProcessing()` registers a default `FileSystemFileStore` automatically. By default it stores uploaded files under `App_Data\Documents`, and each upload gets a new GUID-based stored file name while `AIDocument.FileName` keeps the original user upload name.

Configure a different local base path:

```csharp
builder.Services.Configure<DocumentFileSystemFileStoreOptions>(options =>
{
    options.BasePath = "App_Data/CustomDocuments";
});
```

Hosts can replace `IDocumentFileStore` entirely to change where uploaded files are written:

```csharp
builder.Services.AddSingleton<IDocumentFileStore, AzureBlobDocumentFileStore>();
```

`IDocumentFileStore` extends the general `IFileStore` abstraction. `AIDocument.StoredFileName` and `AIDocument.StoredFilePath` preserve the backing file-store location so hosts can trace and delete the physical file later.

## Document Processing Pipeline

### Step 1 — Upload and Store

When a file is uploaded, a new `AIDocument` record is created in `IAIDocumentStore`:

```csharp
public sealed class AIDocument : CatalogItem
{
    public string ReferenceId { get; set; }    // Owning resource (e.g., chat interaction ID)
    public string ReferenceType { get; set; }  // Resource type (e.g., "chatinteraction")
    public string FileName { get; set; }       // Original file name
    public string ContentType { get; set; }    // MIME type
    public long FileSize { get; set; }         // Size in bytes
    public DateTime UploadedUtc { get; set; }  // Upload timestamp
}
```

The `ReferenceId` and `ReferenceType` pair ties the document to an owning resource. Common reference types include:

| Constant | Value | Meaning |
|----------|-------|---------|
| `AIReferenceTypes.Document.Profile` | `"profile"` | Document attached to an AI profile |
| `AIReferenceTypes.Document.ChatInteraction` | `"chatinteraction"` | Document attached to a chat interaction |
| `AIReferenceTypes.Document.ChatSession` | `"chatsession"` | Document attached to a chat session |

Hosts can layer extra behavior on top of this shared pipeline, but the default follow-up indexing step now lives in the framework as `DefaultAIDocumentIndexingService`. Hosts can call that service after persisting `AIDocument` and `AIDocumentChunk` records so uploaded chunks are mirrored into the configured AI Documents vector index without duplicating provider-specific index management code.

### Step 2 — Read File Content

An `IngestionDocumentReader` is resolved as a keyed service using the file extension. The reader extracts plain text from the file:

```csharp
public abstract class IngestionDocumentReader
{
    public abstract Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default);
}
```

### Step 3 — Normalize and Chunk

The extracted text is normalized (whitespace, encoding) and split into chunks. Each chunk becomes an `AIDocumentChunk`:

```csharp
public sealed class AIDocumentChunk : CatalogItem
{
    public string AIDocumentId { get; set; }   // Parent document ID
    public string ReferenceId { get; set; }    // Denormalized from parent
    public string ReferenceType { get; set; }  // Denormalized from parent
    public string Content { get; set; }        // Chunk text
    public float[] Embedding { get; set; }     // Vector embedding
    public int Index { get; set; }             // Chunk order within the document
}
```

The `ReferenceId` and `ReferenceType` are denormalized from the parent document for efficient query access without joins.

### Step 4 — Generate Embeddings

If the file extension is **embeddable** (see [Built-in Document Readers](#built-in-document-readers)), each chunk is converted to a vector via `IEmbeddingGenerator<string, Embedding<float>>`:

```csharp
var embeddingGenerator =
    await processingService.CreateEmbeddingGeneratorAsync("OpenAI", "default");
```

The generator is created from the configured provider and connection. Embeddings are stored on the chunk itself (`Embedding` property) so they survive index rebuilds.

### Step 5 — Index in Vector Store

Chunks with embeddings are pushed to the search index via `ISearchDocumentManager`:

```csharp
public interface ISearchDocumentManager
{
    Task<bool> AddOrUpdateAsync(
        IIndexProfileInfo profile,
        IReadOnlyCollection<IndexDocument> documents,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        IIndexProfileInfo profile,
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default);

    Task DeleteAllAsync(
        IIndexProfileInfo profile,
        CancellationToken cancellationToken = default);
}
```

Implementations are registered as keyed services by provider name (e.g., `"Elasticsearch"`, `"AzureAISearch"`).

### Step 6 — Query-Time Retrieval

During a conversation, `SearchDocumentsTool` calls `IVectorSearchService` to find the most relevant chunks:

```csharp
public interface IVectorSearchService
{
    Task<IEnumerable<DocumentChunkSearchResult>> SearchAsync(
        IIndexProfileInfo indexProfile,
        float[] embedding,
        string referenceId,
        string referenceType,
        int topN,
        CancellationToken cancellationToken = default);
}
```

The user's query is embedded, and the resulting vector is compared against indexed chunks using cosine similarity.

For uploaded chat-interaction and chat-session text documents, the framework now switches between two context-loading strategies automatically:

- targeted questions continue to use semantic chunk retrieval (`SearchDocumentsTool` and preemptive RAG)
- whole-document tasks such as summarizing, reviewing, rewriting, translating, or extracting complete information from an attached file inject the full document text instead of a few chunks

That keeps RAG efficient for lookup-style questions while avoiding partial-context answers for requests that depend on the entire uploaded file. Tabular uploads are excluded from raw full-document injection and routed to the Tabular Data Agent instead.

## Built-in Document Readers

| Reader | Extensions | Embeddable | Notes |
|--------|-----------|------------|-------|
| `PlainTextIngestionDocumentReader` | `.txt`, `.md`, `.json`, `.xml`, `.html`, `.htm`, `.log`, `.yaml`, `.yml` | Yes | UTF-8 stream reader |
| `PlainTextIngestionDocumentReader` | `.csv` | No | Tabular — handled by the Tabular Data Agent |
| `OpenXmlIngestionDocumentReader` | `.docx`, `.pptx` | Yes | Uses `DocumentFormat.OpenXml` SDK |
| `OpenXmlIngestionDocumentReader` | `.xlsx` | No | Tabular — handled by the Tabular Data Agent |
| `PdfIngestionDocumentReader` | `.pdf` | Yes | Uses `UglyToad.PdfPig` with DocstrumBoundingBoxes |

**Embeddable** means the content is chunked and vector-embedded for semantic search. **Tabular** formats are chunked for retrieval by the tabular SQL workspace but not vector-embedded; the Tabular Data Agent loads them into an in-memory SQL database for querying.

## Custom Document Reader

Register a reader for additional file formats:

```csharp
builder.Services.AddCrestAppsIngestionDocumentReader<RtfIngestionDocumentReader>(
    new ExtractorExtension(".rtf", embeddable: true));
```

Implement the reader:

```csharp
public sealed class RtfIngestionDocumentReader : IngestionDocumentReader
{
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        // Parse the RTF stream into plain text
        using var reader = new StreamReader(source);
        var rawContent = await reader.ReadToEndAsync(cancellationToken);
        var plainText = StripRtfFormatting(rawContent);

        return new IngestionDocument
        {
            Content = plainText,
            Identifier = identifier,
        };
    }
}
```

### `ExtractorExtension`

The `ExtractorExtension` type defines a file extension, whether its content is embeddable, and whether it is tabular:

```csharp
public sealed class ExtractorExtension
{
    public string Extension { get; }   // Normalized with leading dot (e.g., ".rtf")
    public bool Embeddable { get; }    // Whether embeddings should be generated
    public bool IsTabular { get; }     // Whether the extension is loaded into the tabular SQL workspace

    public ExtractorExtension(string extension, bool embeddable = true, bool isTabular = false);
}
```

There is an implicit conversion from `string` to `ExtractorExtension` (with `embeddable: true` by default), so you can pass bare strings for embeddable extensions:

```csharp
// These are equivalent:
services.AddCrestAppsIngestionDocumentReader<MyReader>(".rtf");
services.AddCrestAppsIngestionDocumentReader<MyReader>(new ExtractorExtension(".rtf", true));

// For non-embeddable extensions, use the explicit constructor:
services.AddCrestAppsIngestionDocumentReader<MyReader>(new ExtractorExtension(".tsv", false));

// For tabular extensions, mark the extension as tabular:
services.AddCrestAppsIngestionDocumentReader<MyReader>(
    new ExtractorExtension(".tsv", embeddable: false, isTabular: true));
```

### `AddCrestAppsIngestionDocumentReader<T>`

```csharp
public static IServiceCollection AddCrestAppsIngestionDocumentReader<T>(
    this IServiceCollection services,
    params ExtractorExtension[] supportedExtensions)
    where T : IngestionDocumentReader;
```

This method:
1. Registers the reader as a singleton
2. Registers a keyed singleton for each extension (used to resolve the right reader at runtime)
3. Adds the extensions to `ChatDocumentsOptions`

## Document Tools

Three system tools are automatically available when documents are attached to a session. They are registered with `AIToolPurposes.DocumentProcessing` and injected by `DocumentOrchestrationHandler`.

### `SearchDocumentsTool`

**Name:** `search_documents` (`SystemToolNames.SearchDocuments`)

Performs semantic vector search across all uploaded documents for the current session and returns the most relevant text chunks.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `query` | `string` | Yes | The search query to find relevant content |
| `top_n` | `integer` | No | Number of top matching chunks to return (default: 3) |

### `ReadDocumentTool`

**Name:** `read_document` (`SystemToolNames.ReadDocument`)

Reads the full text content of a specific uploaded document. Truncates output to 50 KB maximum.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `document_id` | `string` | Yes | The unique identifier of the document to read |

### Tabular Data Agent

Tabular files (such as CSV and Excel) are **not** read row-by-row into the prompt. Instead they are
handled by the always-available, system **Tabular Data Agent**, which the primary model can
delegate to like any other agent. The agent loads each file lazily into an in-memory SQLite
database and exposes SQL tools so it can answer questions, run calculations, and manipulate the
data while returning only minimal results to the model — keeping token usage low even for very
large files. The originally uploaded file is always preserved; manipulations apply to the
in-memory copy only. Its system prompt is sourced from the embedded `tabular-data-agent` AI
template, so the wording stays decoupled from the code.

The agent uses four tools that are hidden from the user-facing tool picker — they are referenced by
name only by the agent itself, never selectable in another profile:

| Tool | Name | Purpose |
|------|------|---------|
| `ListTabularDataTool` | `list_tabular_data` | Lists the tabular tables, their columns, and row counts |
| `QueryTabularDataTool` | `query_tabular_data` | Runs a read-only `SELECT` and returns a compact result |
| `ExecuteTabularCommandTool` | `execute_tabular_command` | Applies an `INSERT`/`UPDATE`/`DELETE`/`ALTER` to the in-memory copy |
| `ExportTabularDataTool` | `export_tabular_data` | Exports the current in-memory workspace to a generated download in the original file's format; omit `sql` to export the entire current table, or pass a `SELECT` to shape a subset |

When the user asks for a new version of a tabular file — for example "sort by column A" or
"add column ABC and give me the file" — the agent applies any requested workspace changes with
`execute_tabular_command`, then calls `export_tabular_data`. **The export reflects the current
in-memory data, including every applied mutation, not the originally uploaded file.** Omit the optional
`sql` argument to export the entire current table (the common case when the user wants "the updated
file"); pass a read-only `SELECT` only when the user wants a shaped subset. The exported header row uses
the original source column names where available. **The export preserves the original upload format by
default**: if the source
file was an `.xlsx` workbook the download is an `.xlsx` workbook, and if it was a `.csv` the download
is a `.csv`. Pass the optional `format` argument (for example `csv`, `xlsx`, or `pdf`) to override the
target format when the user explicitly asks for a different one. The chosen format must have a
registered [generated-file writer](#generated-file-downloads); when no writer matches the original
extension the export falls back to `.csv`. The generated file is stored as a new `AIDocument` under the
current chat session or chat interaction and returned through the same authenticated
`AddDownloadAIDocumentEndpoint()` link path as uploaded-document citations. Because the file is
persisted to the shared `IDocumentFileStore` (under a collision-free random storage name) rather than
streamed from memory, it stays re-downloadable across workspace eviction and process restarts — the
in-memory tabular database does not need to be rebuilt to serve the download again. The `[doc:n]`
reference is saved with the assistant message, so reopening the session re-renders the same download
link. The generated file lives in document storage until its owning chat session or chat interaction is
deleted, at which point it is cleaned up automatically (see [Conversation Document
Cleanup](#conversation-document-cleanup)). Because the export runs
inside the Tabular Data Agent and
the primary model may not echo the `[doc:n]` marker in its final reply, the generated file is flagged
with `AICompletionReference.IsGenerated`, so the chat UI always surfaces it as a download even when it
is not cited inline. Export SQL is still validated by `TabularSqlGuard`, so it
cannot use `ATTACH`, `DETACH`, `PRAGMA`, `VACUUM`, extension loading, or batched statements to reach
host files or data outside the scoped in-memory tabular workspace.

The in-memory database is cached per active tabular scope: chat interaction, chat session, or profile
document set. It is built lazily on the first tabular tool call, reused by later prompts while the
same user/session remains active, and disposed after a sliding idle timeout
(`TabularWorkspaceOptions.WorkspaceSlidingExpiration`, five minutes by default). The parsed tabular
document artifact is also stored through `ITabularDocumentArtifactStore` using the configured
`IDocumentFileStore`, so another application instance can hydrate from the shared artifact instead of
reparsing the uploaded chunks. After each successful `execute_tabular_command`, the mutated table state
is snapshotted back to `ITabularDocumentArtifactStore` (best-effort) so the in-memory edits survive
workspace eviction, a process restart, or being served by another instance — a later export still
returns the updated data. The originally uploaded file in `IDocumentFileStore` is never modified. A hosted cleanup service scans for idle workspaces
(`WorkspaceCleanupInterval`, one minute by default), and document uploads/removals plus chat
interaction/session deletion invalidate matching workspaces immediately. Distributed hosts can replace
`ITabularWorkspaceInvalidationPublisher` to broadcast those invalidations through a backplane
(Redis, Service Bus, etc.); the default publisher clears only the local instance. Concurrent users and
sessions do not share a workspace; the cache key includes the scoped reference and attached tabular
document IDs.

**Supported extensions:** any allowed document extension registered with `ExtractorExtension.IsTabular`
(by default `.csv` and `.xlsx`). See [Built-in Document Readers](#built-in-document-readers).

See the [AI Agents](./agents.md) guide for how always-available agents work.

## Generated File Downloads

Beyond tabular exports, the Documents module exposes a general **content-generation** capability that
lets the primary model turn any generated content into a downloadable file — for example a PDF summary,
a Word report, a Markdown document, an HTML page, or a CSV/Excel sheet. This works just like the
[chart tool](./tools.md): the model calls a tool, the content is materialized, and the chat UI surfaces
a download link.

### `GenerateFileTool`

`GenerateFileTool` (`generate_file`) is registered with `AIToolPurposes.ContentGeneration`, so — like
the chart tool — it is **always available** and is not gated on documents being attached to the session.
It accepts:

| Argument | Required | Purpose |
|----------|----------|---------|
| `content` | Yes | The body to place in the file. Markdown/plain text for document formats; CSV text (with a header row) for spreadsheet/CSV formats |
| `file_name` | No | File name shown to the user; its extension determines the output format (for example `summary.pdf`) |
| `format` | No | Output format/extension used only when `file_name` has no extension (for example `pdf`, `docx`, `md`, `html`, `csv`, `xlsx`) |
| `title` | No | Optional heading for the generated document |

The tool resolves the target extension, validates that a writer is registered for it, materializes the
file through `IGeneratedDocumentService`, stores it as a new generated `AIDocument` under the active
conversation, and returns a `[doc:N]` marker that the UI renders as a download link. When the requested
format is one of the tabular formats (`.csv`, `.tsv`, `.xlsx`), the supplied CSV `content` is parsed into
header and rows so the spreadsheet/CSV writers can emit structured cells. The tool requires the Documents
module so that the generated-download path is available; if no chat interaction or chat session scope is
active it returns a friendly error instead of failing.

### `IGeneratedFileWriter`

File materialization is pluggable. Each format is handled by an `IGeneratedFileWriter` keyed by file
extension and registered with `AddGeneratedFileWriter<T>(params extensions)`:

```csharp
public interface IGeneratedFileWriter
{
    Task WriteAsync(GeneratedFileContent content, Stream destination, CancellationToken cancellationToken = default);
}
```

`GeneratedFileContent` is format-agnostic — writers consume whichever parts are relevant: document-style
writers render `Title`/`Text`, while tabular writers render `Header`/`Rows`. `IGeneratedFileWriterResolver`
resolves the writer for a normalized extension and exposes the set of `SupportedExtensions`.

| Writer | Module | Extensions |
|--------|--------|------------|
| `DelimitedGeneratedFileWriter` | `CrestApps.Core.AI.Documents` | `.csv` |
| `PlainTextGeneratedFileWriter` | `CrestApps.Core.AI.Documents` | `.txt`, `.md`, `.json`, `.xml`, `.html`, `.htm`, `.yaml`, `.yml`, `.log` |
| `SpreadsheetGeneratedFileWriter` | `CrestApps.Core.AI.Documents.OpenXml` | `.xlsx` |
| `WordGeneratedFileWriter` | `CrestApps.Core.AI.Documents.OpenXml` | `.docx` |
| `PdfGeneratedFileWriter` | `CrestApps.Core.AI.Documents.Pdf` | `.pdf` |

Register additional formats by implementing `IGeneratedFileWriter` and calling
`services.AddGeneratedFileWriter<MyWriter>(".rtf")`. The `.xlsx`/`.docx` writers require the OpenXml
module and the `.pdf` writer requires the Pdf module.

:::note
The PDF writer uses PDFsharp/MigraDoc. On Windows and WSL2 it relies on the installed system fonts.
On a headless Linux container without fonts you must register a custom `IFontResolver` (via PDFsharp's
`GlobalFontSettings.FontResolver`) so PDF generation can locate a usable font.
:::

### Conversation Document Cleanup

Documents attached to a conversation — both uploaded files and AI-generated downloads such as tabular
exports — are stored in the shared `IDocumentFileStore` and persisted as `AIDocument` records so they
remain available long after the in-memory tabular workspace is evicted. They are bound to their owning
conversation through the `AIReferenceTypes.Document.ChatSession` /
`AIReferenceTypes.Document.ChatInteraction` reference type and removed automatically when that
conversation is deleted.

`IConversationDocumentCleanupService` (default `DefaultConversationDocumentCleanupService`) performs the
removal. For every `AIDocument` returned by `IAIDocumentStore.GetDocumentsAsync(referenceId,
referenceType)` it deletes the document chunks, the parsed tabular artifact in
`ITabularDocumentArtifactStore`, the stored file in `IDocumentFileStore`, and the `AIDocument` record
itself, so nothing is left orphaned in document storage.

The cleanup is wired into the conversation lifecycle on both sides:

- **Chat interactions** delete through the catalog manager, so `ChatInteractionDocumentCleanupHandler`
  (an `ICatalogEntryHandler<ChatInteraction>`) runs the cleanup in its `DeletedAsync` callback.
- **Chat sessions** are deleted by `IAIChatSessionManager`, whose YesSql and EntityCore implementations
  invoke the cleanup service from `DeleteAsync` and `DeleteAllAsync`. The dependency is injected as an
  `IEnumerable<IConversationDocumentCleanupService>` so hosts that register the data stores without the
  Documents module simply perform no cleanup.

## Implementing Stores

The framework defines two store interfaces. You must provide implementations for your persistence layer.

### `IAIDocumentStore`

```csharp
public interface IAIDocumentStore : ICatalog<AIDocument>
{
    Task<IReadOnlyCollection<AIDocument>> GetDocumentsAsync(
        string referenceId,
        string referenceType);
}
```

Inherits CRUD operations from `ICatalog<T>`:

| Method | Description |
|--------|-------------|
| `CreateAsync(T)` | Insert a new document record |
| `UpdateAsync(T)` | Update an existing document record |
| `DeleteAsync(T)` | Delete a document record |
| `FindByIdAsync(string)` | Find a document by its `ItemId` |
| `GetAllAsync()` | Retrieve all documents |
| `GetAsync(IEnumerable<string>)` | Retrieve documents by IDs |
| `PageAsync(int, int, TQuery)` | Paginated query |
| `SaveChangesAsync()` | Flush pending changes |

### `IAIDocumentChunkStore`

```csharp
public interface IAIDocumentChunkStore : ICatalog<AIDocumentChunk>
{
    Task<IReadOnlyCollection<AIDocumentChunk>> GetChunksByAIDocumentIdAsync(string documentId);
    Task<IReadOnlyCollection<AIDocumentChunk>> GetChunksByReferenceAsync(
        string referenceId, string referenceType);
    Task DeleteByDocumentIdAsync(string documentId);
}
```

### Registration

Register your implementations with the DI container:

```csharp
builder.Services.AddScoped<IAIDocumentStore, YesSqlAIDocumentStore>();
builder.Services.AddScoped<IAIDocumentChunkStore, YesSqlAIDocumentChunkStore>();
```

See [Data Storage](./data-storage.md) for more on the catalog pattern and YesSql index conventions.

## Orchestration Integration

`DocumentOrchestrationHandler` implements `IOrchestrationContextBuilderHandler` and is registered automatically by `AddCoreAIDocumentProcessing()`.

```csharp
public sealed class DocumentOrchestrationHandler : IOrchestrationContextBuilderHandler
{
    public Task BuildingAsync(OrchestrationContextBuildingContext context);
    public Task BuiltAsync(OrchestrationContextBuiltContext context);
}
```

During context building, the handler:

1. Checks if the current session has documents (via `ReferenceId` / `ReferenceType`)
2. If documents exist, sets `AICompletionContextKeys.HasDocuments = true`
3. Discovers all tools with purpose `AIToolPurposes.DocumentProcessing` and adds them to the tool set
4. Enriches the system message with document metadata so the model knows what content is available

This means document tools are **only** injected when the session actually has documents — no wasted tokens on tool descriptions when there are no documents.

## Tabular Data

Non-embeddable files (such as CSV and Excel) receive special processing.

### `ITabularBatchProcessor`

Splits large tabular content into batches, processes each batch with the LLM, and merges results:

```csharp
public interface ITabularBatchProcessor
{
    IList<TabularBatch> SplitIntoBatches(string content, string fileName);

    Task<IList<TabularBatchResult>> ProcessBatchesAsync(
        IList<TabularBatch> batches,
        string userPrompt,
        TabularBatchContext context,
        CancellationToken cancellationToken = default);

    string MergeResults(IList<TabularBatchResult> results, bool includeHeader = true);
}
```

### `ITabularBatchResultCache`

Caches batch results to avoid re-processing identical queries:

```csharp
public interface ITabularBatchResultCache
{
    string GenerateCacheKey(string interactionId, string documentContentHash, string prompt);
    string ComputeDocumentContentHash(IEnumerable<(string FileName, string Content)> documents);
    TabularBatchCacheEntry TryGet(string cacheKey);
    void Set(string cacheKey, TabularBatchCacheEntry entry, TimeSpan? expiration = null);
    void Remove(string cacheKey);
    void InvalidateForInteraction(string interactionId);
}
```

When documents are added or removed from an interaction, call `InvalidateForInteraction` to clear stale cache entries.

## Configuration

### `ChatDocumentsOptions`

Controls which file types can be uploaded and how they are processed:

```csharp
services.Configure<ChatDocumentsOptions>(options =>
{
    // Add an embeddable extension
    options.Add(".rtf", embeddable: true);

    // Add a tabular (non-embeddable) extension
    options.Add(".tsv", embeddable: false, isTabular: true);
});
```

| Property | Type | Description |
|----------|------|-------------|
| `AllowedFileExtensions` | `IReadOnlySet<string>` | Complete set of uploadable file extensions |
| `EmbeddableFileExtensions` | `IReadOnlySet<string>` | Subset that gets vector-embedded |
| `TabularFileExtensions` | `IReadOnlySet<string>` | Subset that is loaded into the Tabular Data Agent SQL workspace |
| `MaxVisionInputBytesPerRequest` | `long` | Maximum total image bytes attached to one multimodal request; set `0` or less to disable the limit |

Extensions not in `EmbeddableFileExtensions` are still allowed for upload and can be read by `ReadDocumentTool` or, for tabular files, queried through the Tabular Data Agent, but they are not vector-embedded.

### `InteractionDocumentSettings`

Per-interaction settings for document search:

```csharp
public sealed class InteractionDocumentSettings
{
    public string IndexProfileName { get; set; }  // Index profile for embedding and search
    public int TopN { get; set; } = 3;             // Top matching chunks to include in context
}
```

### Limits

- Maximum **25,000 characters** total for embedding per session
- `ReadDocumentTool` truncates output to **50 KB**
- The Tabular Data Agent returns at most **100 rows** per query by default (`TabularWorkspaceOptions.MaxRowsPerQuery`)
- Tabular exports write at most **1,000,000 rows** by default (`TabularWorkspaceOptions.MaxRowsPerExport`)
- Cached tabular workspaces expire after **5 idle minutes** by default (`TabularWorkspaceOptions.WorkspaceSlidingExpiration`)

## Services Registered by `AddCoreAIDocumentProcessing()`

| Service | Implementation | Lifetime | Purpose |
|---------|---------------|----------|---------|
| `IAIDocumentProcessingService` | `DefaultAIDocumentProcessingService` | Scoped | Orchestrates document processing |
| `ITabularBatchProcessor` | `TabularBatchProcessor` | Scoped | Processes CSV/Excel batch queries |
| `ITabularBatchResultCache` | `TabularBatchResultCache` | Singleton | Caches tabular query results |
| `DocumentOrchestrationHandler` | — | Scoped | Injects document context into orchestration |
| `PlainTextIngestionDocumentReader` | — | Singleton | `.txt`, `.csv`, `.md`, `.json`, `.xml`, `.html`, `.htm`, `.log`, `.yaml`, `.yml` |
| `OpenXmlIngestionDocumentReader` | — | Singleton | `.docx`, `.xlsx`, `.pptx` |
| `PdfIngestionDocumentReader` | — | Singleton | `.pdf` |
| `SearchDocumentsTool` | — | System tool | Semantic vector search |
| `ReadDocumentTool` | — | System tool | Full document read |
| `GenerateFileTool` | — | System tool | Always-available `generate_file` content-generation tool that produces downloadable files |
| `IGeneratedDocumentService` | `DefaultGeneratedDocumentService` | Scoped | Materializes generated content into a downloadable `AIDocument` and emits a `[doc:N]` reference |
| `IConversationDocumentCleanupService` | `DefaultConversationDocumentCleanupService` | Scoped | Removes a conversation's documents, stored files, tabular artifacts, and chunks when the chat session or chat interaction is deleted |
| `IGeneratedFileWriterResolver` | `GeneratedFileWriterResolver` | Singleton | Resolves the `IGeneratedFileWriter` for a file extension |
| `IGeneratedFileWriter` (keyed) | `DelimitedGeneratedFileWriter`, `PlainTextGeneratedFileWriter` | Singleton | Core writers for `.csv` and plain-text formats |
| `ITabularDocumentArtifactStore` | `DocumentFileStoreTabularDocumentArtifactStore` | Singleton | Stores parsed tabular document artifacts in shared document storage |
| `TabularWorkspaceCache` | — | Singleton | Reuses in-memory tabular databases per active chat scope with sliding expiration |
| `TabularWorkspaceCleanupBackgroundService` | — | Singleton hosted service | Disposes idle cached tabular workspaces |
| `ITabularWorkspaceInvalidator` | `TabularWorkspaceCache` | Singleton | Invalidates cached workspaces when source chat scopes change or are deleted |
| `ITabularWorkspaceInvalidationPublisher` | `LocalTabularWorkspaceInvalidationPublisher` | Singleton | Publishes workspace invalidation events; replace for distributed fan-out |
| `IAIProfileProvider` | `TabularDataAgentProvider` | Scoped | Contributes the system Tabular Data Agent |
| `list_tabular_data` / `query_tabular_data` / `execute_tabular_command` / `export_tabular_data` | — | Hidden tools | Tabular Data Agent SQL tools (referenced by the agent, hidden from the picker) |
