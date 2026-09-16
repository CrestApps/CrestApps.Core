# PDF Ingestion, Figures, Typed Knowledge & Indexers — Implementation Plan

**Status:** Design, verified against the tree at `d733a534`. No feature code has been written.
**Audience:** the engineer or model that will implement this. Every phase in section 19 is written
so it can be executed without re-deriving the design: files to touch, steps in order, the tests
that prove it, and the condition under which the phase is done.

**Goals, in priority order:**

1. **Robust ingestion.** A PDF (and, through the same pipeline, any registered file type) produces
   faithful reading-order text and recovers what today lives only inside images — captions,
   chart axes, printed values — so a figure-dependent document is actually answerable.
2. **Typed knowledge in the AI data source.** Figures, charts, tables and text are stored as
   *separate, individually retrievable rows* of one knowledge-base index, each carrying its type,
   parent and page, so retrieval can return "the chart on p. 8" as its own hit and a client can
   fetch the underlying image as a reference.
3. **Exposure over MCP.** Search results carry resource links to figures; a resource template
   serves the bytes; one `get_source` tool resolves any canonical id.
4. **Indexers.** Content arrives from web, Azure Blob, FTP/SFTP or local folders through one
   connector abstraction, managed as *indexers* an operator creates, schedules, points at a data
   source, and equips with the models (vision, utility, embedding) to use.
5. **Zero regression.** A plain-text PDF uploaded to chat, an `.xlsx` upload, every existing
   data source type and every existing web crawler must behave exactly as today — or strictly
   better — after every phase. Section 21 is the audit; every phase repeats the pinned tests.

---

## 0. How to execute this plan

### 0.1 Mandatory reading before touching code

1. `.github/copilot-instructions.md` — the repository's coding rules. They are enforced by review
   and by `TreatWarningsAsErrors`. The ones most often violated by generated code:
   - XML `<summary>` on **every** public type, member, constructor and parameter (`<param>` in
     signature order); a blank line before every `<summary>` block unless preceded by `{`.
   - `sealed` on every public class unless inheritance is intended.
   - **No** `ArgumentNullException.ThrowIf...` in constructors; **do** guard public method inputs,
     then one blank line after the last guard.
   - A blank line before `return` unless it is the first statement in a block; blank lines around
     `if` / loops / `switch`; never two consecutive blank lines; exactly one trailing newline.
   - Inject `TimeProvider`; never `DateTime.UtcNow`.
   - Multi-parameter constructors span multiple lines; `: base(...)` on its own indented line.
   - Catalog entry models get an authoritative `CatalogEntryHandlerBase<T>` with `PopulateAsync`
     using the `JsonNodeExtensions` helpers in `src/Utilities/CrestApps.Core.Support/JsonNodeExtensions.cs`
     (`TryUpdateTrimmedStringValue`, `TryGetBooleanValue`, `TryGetNullableInt32Value`, ...).
   - Prefer extending shared abstractions over adding a one-off provider or store.
2. `AGENTS.md` — build, test and docs discipline.
3. This document's **Appendix A** — the verified API surface. Do not rely on memory of PdfPig or
   `Microsoft.Extensions.DataIngestion`; both have counter-intuitive behaviour that is spelled out
   there because it was measured.

### 0.2 Build and test

Run from the repository root. Every phase ends with both commands green and no new warnings.

```bash
dotnet build .\CrestApps.Core.slnx -c Release /p:NuGetAudit=false
dotnet test .\tests\CrestApps.Core.Tests\CrestApps.Core.Tests.csproj -c Release /p:NuGetAudit=false
```

`dotnet build` on the `.slnx` has a known spurious `CS0234` in the Resilience project; if it
appears, build the affected projects individually to confirm the real state.

### 0.3 Test conventions in this repository (verified)

- Framework: **xunit.v3 4.0.0** with **Moq 4.20.72**. Cancellation: `TestContext.Current.CancellationToken`.
- Naming: `Method_Scenario_Expectation` (`ReadAsync_HappyPath_ReadsTextFromGeneratedPdf`).
- Location: `tests/CrestApps.Core.Tests/Core/<Area>/...`. PDF reader tests already live at
  `tests/CrestApps.Core.Tests/Core/Documents/Services/PdfIngestionDocumentReaderTests.cs`; a second,
  older copy exists at `tests/CrestApps.Core.Tests/Helpers/DocumentReaders/PdfIngestionDocumentReaderTests.cs`.
  **Extend the `Core/Documents/Services` file; keep the `Helpers` file compiling.**
- PDF fixtures are generated in-test with PdfPig's writer (`UglyToad.PdfPig.Writer.PdfDocumentBuilder`).
  **Do not add PDFsharp to the test project and do not commit binary PDFs.** The reference document
  used for the evidence in section 2 is third-party copyrighted material and, per repository policy,
  no customer or client material goes into the repository — fixtures are synthetic, always.
- Web crawler behaviour is pinned by 23 tests in `tests/CrestApps.Core.Tests/Core/WebCrawlers/WebCrawlerTests.cs`
  (planner, handler, reindex service, sitemap strategy, HTML reader). Part 3 must keep every one
  of them green, renamed but semantically unchanged.
- No tests exist today for `DefaultAIDataSourceIndexingService`. Phase 4 adds them.

### 0.4 Rules of engagement for the executing model

1. **One phase, one pull request.** Do not start phase N+1 in the PR for phase N.
2. **Write the pinned tests first** (section 21.5) in phase 0, before changing any behaviour.
   They are the regression contract for every later phase.
3. **Verify third-party members by compiling**, not by recall. When an overload in Appendix A is
   marked *verify*, open the XML doc file named there and confirm the parameter list.
4. **Heuristics degrade to today's behaviour, never to something cleverer.** When layout
   analysis, caption assignment or structure inference is uncertain, emit what the current reader
   would have emitted and move on. A wrong guess stored in an index is worse than a plain paragraph.
5. **Never fail an ingest because an optional enrichment failed.** No vision deployment, a vision
   call that throws, a cache miss — log once, continue text-only.
6. **Do not rename persisted names.** Renaming a C# type is fine; renaming a table, collection or
   the `Source` string stored in existing records is a migration and is out of scope.
7. **Every public behaviour change updates the docs** under `src/CrestApps.Core.Docs/docs/` and
   adds a bullet to `src/CrestApps.Core.Docs/docs/changelog/2.0.0.md`.
8. **Ask before widening scope.** If a step turns out to need a change this plan did not list,
   stop and record it under section 22 rather than improvising.

---

## 1. Where we are today

Everything in this section was verified against the tree at `d733a534` and against the
packages pinned in `Directory.Packages.props`: `PdfPig 0.1.16`, `Microsoft.Extensions.DataIngestion
10.4.0-preview.1.26160.2`, `ModelContextProtocol 2.2.0`, `Microsoft.Extensions.AI 10.9.0`.

### 1A. Two disconnected pipelines

| | Chat documents | Data sources |
|---|---|---|
| Entry | `IFormFile` → `DefaultAIDocumentProcessingService.ProcessFileAsync` (`src/Primitives/CrestApps.Core.AI.Documents/Services/DefaultAIDocumentProcessingService.cs`) | `IAIDataSourceSourceHandler.ReadAsync` → `DefaultAIDataSourceIndexingService.IndexDocumentsAsync` (`src/Primitives/CrestApps.Core.AI/Services/DefaultAIDataSourceIndexingService.cs`) |
| Reader | keyed `IngestionDocumentReader` **by file extension** (`GetKeyedService<IngestionDocumentReader>(extension)`) | none — projection sources read an *index*; `Web` reads pages through `IWebCrawlerStrategy` |
| Processors | none registered | none |
| Output | `AIDocument` + `AIDocumentChunk` rows (`IAIDocumentChunkStore`) | knowledge-base index rows via `ISearchDocumentManager` |

`AIDataSourceSourceTypes` is `SearchIndexProfile`, `Elasticsearch`, `AzureAISearch`, `PostgreSQL`,
`Web`. **No source type ingests a file.** The two worlds share the reader abstraction and nothing
else.

### 1B. What the PDF reader does

`src/Primitives/CrestApps.Core.AI.Documents.Pdf/Services/PdfIngestionDocumentReader.cs` (~100 lines):

- `pdfPage.Text` — raw content-stream order; interleaves columns and ads.
- One `IngestionDocumentParagraph(pageText) { Text = pageText }` per page inside one
  `IngestionDocumentSection { PageNumber = n }`. It assigns `Text` explicitly — see 1E for why
  that matters.
- No images, no tables, no headers/footers. `Page.GetImages()` is never called.
- Parameterless constructor; registered as a **singleton** by
  `services.AddCoreAIIngestionDocumentReader<PdfIngestionDocumentReader>(".pdf")` in
  `PdfServiceCollectionExtensions.AddCoreAIPdfDocumentProcessing`. Two test files construct it
  with `new PdfIngestionDocumentReader()`.

### 1C. What the knowledge base stores and returns

`DefaultAIDataSourceIndexingService.IndexDocumentsAsync` (line ~268):

- `_textNormalizer.NormalizeAndChunkAsync(sourceDocument.Content)` **unconditionally** — every
  source document is re-chunked at 500 tokens / 50 overlap (`RagTextNormalizer`).
- Row fields, from `DataSourceConstants.ColumnNames` (`src/Primitives/CrestApps.Core.Infrastructure/DataSourceConstants.cs`):
  `chunkId` (= `{referenceId}_{i}`), `referenceId`, `dataSourceId`, `referenceType`, `chunkIndex`,
  `title`, `content`, `embedding`, `timestamp`, and `filters` (= `SourceDocument.Fields`, per
  document).
- Deletion: `BuildChunkIds` enumerates `{referenceId}_0` … `{referenceId}_999` — **a fixed 1000
  ids per reference** — and hands the list to `ISearchDocumentManager.DeleteAsync`.
- The knowledge-base schema is `DataSourceSearchIndexProfileHandler.BuildFields`
  (`src/Primitives/CrestApps.Core.AI/Indexing/DataSourceSearchIndexProfileHandler.cs`). **It does
  not declare `filters`.** PostgreSQL adds a JSONB `filters` column lazily
  (`PostgreSQLSearchDocumentManager.EnsureDataSourceFilterColumnAsync`); Elasticsearch relies on
  dynamic mapping; Azure AI Search creates only the declared fields and the document manager
  writes every `IndexDocument.Fields` entry verbatim. *Whether `filters` currently round-trips
  on Azure is unverified and suspicious — see 21.3.*
- Indexes are created once: `EnsureKnowledgeBaseIndexAsync` returns early when `ExistsAsync`.
  `ISearchIndexManager` has `ExistsAsync`, `ComposeIndexFullName`, `CreateAsync`, `DeleteAsync` —
  **no way to add a field to an existing index.**
- `DataSourceSearchResult` (`src/Abstractions/CrestApps.Core.Infrastructure.Abstractions/Indexing/Models/DataSourceSearchResult.cs`)
  exposes `ReferenceId`, `Title`, `Content`, `ChunkIndex`, `ReferenceType`, `Score`. **No
  `Filters`, no type.** All three providers `Select` only those columns.
- `IDataSourceContentManager` has `SearchAsync(profile, embedding, dataSourceId, topN, filter)`
  and `DeleteByDataSourceIdAsync`. **No delete by reference id.**

### 1D. What MCP can return today

`src/Primitives/CrestApps.Core.AI.Mcp/McpServerBuilderExtensions.cs`, three call-tool return sites
(lines ~193, ~215, ~234):

```csharp
return new CallToolResult { Content = [new TextContentBlock { Text = result?.ToString() ?? string.Empty }] };
```

The tool result is an `object` from `AIFunction.InvokeAsync`; it is always stringified. The SDK
offers `TextContentBlock`, `ImageContentBlock`, `EmbeddedResourceBlock`, `ResourceLinkBlock`,
`BlobResourceContents`, `TextResourceContents`. Resource templates already work:
`DefaultMcpServerResourceService.ListTemplatesAsync` publishes every `McpResource` whose URI has
variables, and `McpResourceTypeHandlerBase` (with `SanitizePath`, `CreateErrorResult`,
`IsTextMimeType`) is the handler base — `FtpResourceTypeHandler` is the model to copy.

### 1E. What `Microsoft.Extensions.DataIngestion` gives us — measured

```text
IngestionDocument(string identifier)      Identifier, Sections (IList<IngestionDocumentSection>), EnumerateContent()
IngestionDocumentElement                  Text {get;set;}  PageNumber int? {get;set;}  Metadata IDictionary<string,object>  HasMetadata  GetMarkdown()
 |- IngestionDocumentSection()             Elements (IList<IngestionDocumentElement>)
 |- IngestionDocumentParagraph(markdown)
 |- IngestionDocumentHeader(markdown) / IngestionDocumentFooter(markdown)
 |- IngestionDocumentTable(markdown, IngestionDocumentElement[,] cells)   Cells
 |- IngestionDocumentImage(markdown)       Content ReadOnlyMemory<byte>?  MediaType  AlternativeText
abstract IngestionDocumentReader          Task<IngestionDocument> ReadAsync(Stream, string identifier, string mediaType, CancellationToken)
abstract IngestionDocumentProcessor       Task<IngestionDocument> ProcessAsync(IngestionDocument, CancellationToken)
internal IngestionDocumentElementExtensions.GetSemanticContent()          <-- NOT callable from our code
```

Three facts that are easy to get wrong and were each confirmed by executing code against the
package:

1. **`GetSemanticContent()` is `internal`** — the class and the method. `element.GetSemanticContent()`
   does not compile. We ship our own (section 9). Its measured behaviour, which ours reproduces:
   paragraph → `Text`; image with `AlternativeText` → the alt text; image without → `null`;
   table → `Text`.
2. **`Text` is a plain settable property that defaults to `null`.** It is not derived from the
   constructor's markdown: `new IngestionDocumentParagraph("body").Text` is empty. An element
   whose `Text` is never assigned is invisible to every consumer in this repository.
3. **`EnumerateContent()` does not yield sections** (its own remarks: "Sections themselves are not
   included"). `PageNumber` set on a section is therefore invisible. Page attribution must be set
   **on each element**.

### 1F. What PdfPig 0.1.16 gives us — measured

The `PdfPig` package ships **six assemblies**, including
`UglyToad.PdfPig.DocumentLayoutAnalysis.dll`. No additional package reference is needed. The
exact types and members are in Appendix A.1; the ones the reader rewrite is built on:

- `NearestNeighbourWordExtractor.Instance.GetWords(page.Letters)`
- `DocstrumBoundingBoxes.Instance.GetBlocks(words)` (alternative: `RecursiveXYCut.Instance`)
- `UnsupervisedReadingOrderDetector.Instance.Get(blocks)`
- `DecorationTextBlockClassifier.Get(...)` — **operates across pages** (finds blocks that recur
  page after page), so the reader must segment *all* pages before it can classify any.
- `page.GetImages()` → `IPdfImage` with `Bounds`, `WidthInSamples`, `HeightInSamples`,
  `RawBytes`, `TryGetPng(out byte[])`, `ImageDictionary`, `IsInlineImage`.
- `page.ExperimentalAccess.Paths` for vector geometry; `document.TryGetBookmarks`;
  `document.Information.{Title,Author,Subject,Keywords,Creator,Producer}`.
- `Letter.PointSize`, `FontName`, `BoundingBox`, `TextSequence` for typography.
- Writer, for fixtures: `PdfDocumentBuilder`, `PdfPageBuilder.AddText / AddPng / AddJpeg /
  DrawLine / DrawRectangle`.

### 1G. What already exists and must be reused, not rebuilt

| Need | Existing code | Where |
|---|---|---|
| Vision call with prompt template, deployment resolution and JSON parsing | `IImageAnalysisService` / `DefaultImageAnalysisService`, template `image-analysis` | `src/Primitives/CrestApps.Core.AI.Documents/Services/DefaultImageAnalysisService.cs`, `src/Primitives/CrestApps.Core.AI/Templates/Prompts/image-analysis.md` |
| Vision-capability check | `deployment.TryGet<AIDeploymentMetadata>(out m) && m.SupportsFeature(AIDeploymentFeatureNames.ImageInput)`; `IAIDeploymentCapabilityService.SupportsFeatureOrUnconstrained` | `DefaultImageAnalysisService.ResolveVisionDeploymentAsync`, `src/Abstractions/CrestApps.Core.AI.Abstractions/Capabilities/IAIDeploymentCapabilityService.cs` |
| Storing binary files | `IDocumentFileStore.SaveFileAsync/GetFileAsync/DeleteFileAsync`, `DocumentFileStoragePath.Create(referenceType, referenceId, fileName, subfolder)` | `src/Primitives/CrestApps.Core.AI.Documents/` |
| Background re-index of specific documents | `IAIDataSourceIndexingQueue.QueueSyncDataSourceDocumentsAsync(dataSourceId, ids)` / `QueueRemoveDataSourceDocumentsAsync` | `src/Primitives/CrestApps.Core.AI/Services/IAIDataSourceIndexingQueue.cs` |
| Change detection, scheduling, per-record CRUD, admin UI for a "thing that indexes into a data source" | the whole `WebCrawler` vertical | `src/Primitives/CrestApps.Core.AI.WebCrawlers/`, `src/Stores/*/…WebCrawl*`, `src/Startup/*/…WebCrawlers*` |
| HTML → `IngestionDocument` | `HtmlIngestionDocumentReader` (AngleSharp) | `src/Primitives/CrestApps.Core.DataIngestion/HtmlIngestionDocumentReader.cs` |
| Media type inference and image signature sniffing | `MediaTypeHelper.InferMediaType`, `HasValidImageSignature`, `IsVisionImageMediaType` | `CrestApps.Core.AI.Documents` |
| Secrets on catalog records | `IDataProtectionProvider` pattern in `FtpResourceTypeHandler`; `AIDataSourceSecretProtector` | `CrestApps.Core.AI`, `CrestApps.Core.AI.Mcp.Ftp` |
| OData filter → provider filter | `IODataFilterTranslator` keyed by provider | all three provider projects |
| Tool instance blueprint | `IAIToolInstanceSource`, `DataSourceSearchToolInstanceSource`, `builder.AddSource<T>(name, …)` | `src/Primitives/CrestApps.Core.AI/Tooling/Instances/DataSources/` |

---

## 2. Evidence from the reference document

A Hungarian HVAC trade journal (23 pages), used throughout as the worked example.

| Property | Value |
|---|---|
| Pages | 23 |
| Producer / Creator | Adobe PDF Library 17.0 / InDesign 20.1 (Macintosh) |
| Tagged (`/StructTreeRoot`) | **No** |
| Encrypted | No |
| `ToUnicode` maps | 26 — text extraction is reliable; **not a scan** |
| Image XObjects | 103–113 (85 `DCTDecode`, 0 `JPXDecode`, 0 `CCITTFax`/`JBIG2`) |

Findings that drive design decisions:

1. **Figures carry the data.** p4 `1. ábra` is a scatter plot whose only quantitative payload
   is `y = 1,0892x` / `R² = 0,8858`, rasterized into the JPEG. p8 `3. ábra` is a stacked bar
   chart with axis labels and a six-level legend, likewise pixels only. **None of it is in the
   text layer.**
2. **The prose depends on them.** ~70 caption and cross-reference sites across 23 pages
   (`a 4. ábra trendvonalai`, `az 5. ábra a MESP működését szemlélteti`).
3. **Caption direction is not uniform.** `N. ábra` (figure) sits *below*; `N. táblázat`
   (table) sits *above*. Same document, same publisher. Any fixed rule is wrong.
4. **Most images are worthless.** p19 contains a stock photo of a sunny sky. Roughly 60 of
   the ~103 are logos, ad artwork, or decoration.
5. **Duplicates exist.** p7 references the same 495x693 XObject twice.
6. **Ligature damage.** p19 extracts as `Elektrofi  lterekkel` (U+FB01 plus a spurious space) —
   a search for "elektrofilter" misses.
7. **Tables are mostly real text.** p5 has three tables and zero images.
8. **Ads interleave with articles.** p1 raw text merges masthead, TOC and three ads; p23 is a
   full-page ad with 178 characters of text.
9. **Hungarian throughout** — embeddings and normalization must be accent-safe.
10. **No structure is present in the file.** `outline` / bookmarks count is **0**, there is no
    `/StructTreeRoot`, and `/Title` / `/Author` / `/Subject` are all absent — document
    metadata carries only `CreationDate`, `ModDate`, `Creator`, `Producer`. Any article
    hierarchy must be **inferred**; it does not fall out of the PDF.
11. **But the table of contents is machine-readable.** p2 yields 14 entries carrying author,
    title and start page. This is the practical seed for article segmentation.
12. **The running head is a section-type label.** `LEKTORÁLT CIKK` (peer-reviewed article),
    `SZAKMAI CIKK` (professional article), `TERMÉKAJÁNLÓ` (product showcase). Pages carrying
    none — p2, p23 — are advertisements. This is a far better ad discriminator than text
    density, and it yields an article *type* for free.
13. **Printed page number does not equal PDF page index.** The TOC cites pages up to 39 in a
    23-page PDF. A citation of "p. 85" must come from a folio actually read off the page,
    never from the PDF index.

---

## 3. Decisions

Each decision is numbered so later sections can cite it. A decision is not re-argued in the phase
that implements it; if the implementation shows a decision to be wrong, record that in section 16
and stop.

| # | Decision | Rationale |
|---|---|---|
| **D1** | Build on the `IngestionDocument` element model; do not invent a parallel figure model. | 1E — `IngestionDocumentImage.AlternativeText` and `IngestionDocumentProcessor` already exist. A second model forks us from the library forever. |
| **D2** | Describe figures **at ingest**, never at query time. | Retrieval is a vector search over text. If `R² = 0,8858` is not text in the index, the row is never a candidate. |
| **D3** | Figures reach MCP clients as **`ResourceLinkBlock` in tool results** plus a **resource template**; never as entries in `resources/list`. | `resources/list` is a bounded set a human browses. A knowledge base's figures are an unbounded machine-selected set. |
| **D4** | Caption association is a **scored assignment with a per-document learned prior**, not a fixed rule. | Evidence #3 — figure captions sit below, table captions above, in the same document. |
| **D5** | Vision is gated by **capability AND per-image salience**, in three tiers, with an operator override. | Evidence #1 and #4 — some images are the whole answer, most are worthless. |
| **D6** | The knowledge unit is the **article** (or, when no structure exists, the document); the page is a coordinate on every row, not a container. | "What did the May issue say about fatigue" is answered by an article, not a page. |
| **D7** | Ingest completes **text-first**; figure descriptions **backfill** asynchronously in the indexer path, synchronously but capped in the chat path. | Vision latency must not gate searchability of a 500-page document; a chat upload is one file the user is already waiting on. |
| **D8** | Decompose into **typed knowledge objects** — document, article, text chunk, figure, chart, table — each independently embedded and retrievable, each carrying `rootId`, `parentId`, page. | A page holding 500 words plus a chart plus a table is four pieces of knowledge; one embedding over all of it retrieves none of them well. |
| **D9** | Chart series data is extracted **only where the source contains it** (vector geometry, printed labels); every value carries a confidence; a vision **estimate is never stored as a datum**. | The one correctness hazard: a confident wrong number is worse than no number. |
| **D10** | **One knowledge-base index with a `contentType` discriminator**, not one index per object type. | Three providers must all support it; a discriminator column plus filters gives the same query power portably. |
| **D11** | Article segmentation is **seeded from the table of contents**, refined by heading typography and the running-head label; **degrades to one article per document**. | Evidence #10–#13 — structure is absent from the file but recoverable from its own front matter. |
| **D12** | The managed unit is an **`Indexer`**, a generalization of today's `WebCrawler` record — a rename plus additions, not a new subsystem. | 17.1 — `WebCrawler` already has target, enabled, schedule, strategy key, strategy settings, stores, handler, UI. |
| **D13** | Model selection (vision, utility, embedding) lives **on the indexer** and is **validated at save time** against the deployment's advertised features. | One data source is fed by indexers with very different content; save-time validation makes the `ResolveSlotAsync` "first capable" fallback safe. |
| **D14** | Flattening uses **our own `GetSemanticText`**. | 1E and section 9 — the library's `GetSemanticContent` is `internal`. |
| **D15** | **One `KnowledgeObject` catalog entry and one `IKnowledgeObjectStore`** hold every object type (document, article, text, figure, chart, table). Type-specific detail rides in `Properties`. | Every new catalog entry costs a model, a handler, an EntityCore store, a YesSql store + index + schema builder, a `CatalogRecordFactory` case and two DI registrations (Appendix B). Five entities would be five of each. |
| **D16** | The typed model uses **first-class knowledge-base columns** `contentType`, `rootId`, `parentId`, `page` — added to `DataSourceSearchIndexProfileHandler.BuildFields` and to existing indexes through a new **schema-upgrade step** — not `filters.*`. | 1C / 21.3 — `filters` is not declared in the schema and is stored differently by each provider; Azure's handling is unverified. Correctness cannot depend on it. |
| **D17** | Reuse **`IImageAnalysisService`** for vision, extended with a request object that carries caption, context, language, template and deployment. | 1G — deployment resolution, template rendering, JSON parsing and error handling already exist and are tested in production. |
| **D18** | **One `IAIDocumentIngestionPipeline`** (reader resolution + processors) is shared by the chat-upload path and the indexer path. | Two pipelines drift. The chat path gets every reader and processor improvement for free. |
| **D19** | Processors receive an explicit **`DocumentIngestionContext`** (options + services for this run) through our own `AIDocumentIngestionProcessor` base; the library's `ProcessAsync(document, ct)` overload remains callable with defaults. | `IngestionDocumentProcessor.ProcessAsync` takes only the document; per-run options (figure mode, deployment, budget) have nowhere else to go. Processors are DI singletons. |
| **D20** | A new **`Ingested`** data-source source type is the *only* file-fed source type. Manual upload and indexers both feed it through `IKnowledgeIngestionService`. `Web` keeps its current handler untouched. | An earlier draft had both `Files` and `Ingested`; two types doing the same job is one too many. Leaving `Web` alone is the no-regression path. |
| **D21** | Delete knowledge-base rows **by reference id through the provider** (`IDataSourceContentManager.DeleteByReferenceIdsAsync`, default-implemented as "not supported" so external providers keep compiling), falling back to today's `BuildChunkIds` only when a provider reports it cannot. | 1C — 1000 ids per reference becomes 40,000 ids to delete 40 typed rows. |
| **D22** | Indexer run state is stored **on the indexer record** (`Properties` → `IndexerRunSummary`), not in a run-history store, in the first cut. | One fewer catalog entry (Appendix B). History is an open question (22.10). |

---

# Part 1 — Ingestion

Part 1 changes what an `IngestionDocument` contains and how it is flattened. It touches
`CrestApps.Core.AI.Documents` and `CrestApps.Core.AI.Documents.Pdf` only. Nothing in it changes
the shape of any index or store.

## 4. The ingestion pipeline service (D18, D19)

Today `DefaultAIDocumentProcessingService.ProcessFileAsync` resolves a reader and flattens; no
processors run anywhere. Introduce one service both paths call.

**New, in `src/Primitives/CrestApps.Core.AI.Documents/Ingestion/`:**

```csharp
/// Resolves a reader for the content, reads it, and runs every registered processor in order.
public interface IAIDocumentIngestionPipeline
{
    Task<IngestionDocument> IngestAsync(
        Stream content,
        string fileName,
        string mediaType,
        DocumentIngestionContext context,
        CancellationToken cancellationToken = default);
}

/// Per-run options and services. Immutable after construction.
public sealed class DocumentIngestionContext
{
    public static DocumentIngestionContext Default { get; } = new();
    public FigureProcessingMode FigureMode { get; init; } = FigureProcessingMode.Auto;   // Off | Auto | All
    public string VisionDeploymentName { get; init; }                                    // null = resolve the Vision slot
    public string UtilityDeploymentName { get; init; }                                   // null = resolve the Utility slot
    public int MaxFigureDescriptionsPerDocument { get; init; } = 25;
    public string Language { get; init; }                                                // BCP-47, null = unknown
    public bool DescribeFiguresInline { get; init; } = true;                             // chat path true; indexer path false (D7)
    public string DataSourceId { get; init; }                                            // null in the chat path
}

/// Our processor base. Implement ProcessAsync(document, context, ct); the library overload delegates with Default.
public abstract class AIDocumentIngestionProcessor : IngestionDocumentProcessor
{
    public sealed override Task<IngestionDocument> ProcessAsync(IngestionDocument document, CancellationToken cancellationToken = default)
        => ProcessAsync(document, DocumentIngestionContext.Default, cancellationToken);

    public abstract Task<IngestionDocument> ProcessAsync(IngestionDocument document, DocumentIngestionContext context, CancellationToken cancellationToken);
}

/// Media-type-first reader resolution (section 16.3). Phase 1 ships extension-only behaviour behind this interface
/// so the seam exists; phase 9 adds media type and sniffing (16.3).
public interface IIngestionDocumentReaderResolver
{
    IngestionDocumentReader Resolve(string fileName, string mediaType, Stream content = null);
}
```

**Registration**, in `ServiceCollectionExtensions` of `CrestApps.Core.AI.Documents`:

```csharp
public static IServiceCollection AddCoreAIIngestionDocumentProcessor<T>(this IServiceCollection services)
    where T : AIDocumentIngestionProcessor
{
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IngestionDocumentProcessor, T>());
    return services;
}
```

Processors run in **registration order** (`IEnumerable<IngestionDocumentProcessor>` preserves it).
The default registration order, set in `AddCoreAIDocumentProcessing`, is: caption → salience →
description. Document this order in `docs/core/document-processing.md`; a host that registers its
own processor before calling `AddCoreAIDocumentProcessing` runs first.

**`DefaultAIDocumentProcessingService.ProcessFileAsync`** changes in exactly two places: it calls
`_pipeline.IngestAsync(stream, file.FileName, mediaType, new DocumentIngestionContext { … })`
instead of resolving the reader itself, and it flattens with `GetSemanticText()` (section 9).
Everything after — tabular routing, chunking, `AIDocument` creation — is untouched.

**Tests** (`tests/CrestApps.Core.Tests/Core/Documents/Ingestion/AIDocumentIngestionPipelineTests.cs`):

- `IngestAsync_RunsProcessorsInRegistrationOrder` — two recording processors; assert order.
- `IngestAsync_NoReader_ThrowsNotSupported` — unknown extension; message names the extension.
- `IngestAsync_ProcessorThrows_PropagatesAndDoesNotSwallow` — the pipeline itself does not catch;
  callers decide (the chat path already wraps reading in try/catch and returns `Failed`).
- `LibraryOverload_UsesDefaultContext` — calling `ProcessAsync(document, ct)` on an
  `AIDocumentIngestionProcessor` passes `DocumentIngestionContext.Default`.

## 5. Reader rewrite — `PdfIngestionDocumentReader`

### 5.1 Behaviour

Per document:

1. Open with `PdfDocument.Open(stream)` exactly as today (the seekable/non-seekable buffering and
   the `finally` disposal stay).
2. **Pass A — segment every page.** For each page: `words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters)`;
   `blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words)`; `ordered = UnsupervisedReadingOrderDetector.Instance.Get(blocks)`.
   Keep the per-page `IReadOnlyList<TextBlock>` in a list.
3. **Pass B — decoration.** `decoration = DecorationTextBlockClassifier.Get(allPagesBlocks, …)`
   (*verify overload in Appendix A.1*). Apply the **guard** (5.2). Mark, do not delete: set
   `Metadata[ElementMetadataKeys.IsDecoration] = true` on emitted elements when
   `PdfLayoutOptions.EmitDecorationAsHeaderFooter` is true, otherwise skip them. Default: skip.
4. **Pass C — emit.** For each page, one `IngestionDocumentSection { PageNumber = n }` containing,
   in reading order:
   - `IngestionDocumentParagraph(markdown) { Text = normalizedText, PageNumber = n }` per
     non-decoration block. **Both `Text` and `PageNumber` are set on every element** (1E).
     `Metadata[ElementMetadataKeys.BoundingBox] = new double[] { left, bottom, right, top }`,
     `Metadata[ElementMetadataKeys.ModalPointSize] = <block's modal Letter.PointSize>`,
     `Metadata[ElementMetadataKeys.ModalFontName] = <block's modal FontName>`.
   - `IngestionDocumentImage(markdown)` per `page.GetImages()` result, **from phase 2 on** (5.3).
   - Decoration blocks, when emitted, as `IngestionDocumentHeader` / `IngestionDocumentFooter`
     by vertical position (top third → header).
5. **Text normalization** (5.4) is applied to every `Text` before it leaves the reader.
6. Pages with no elements are not added (today's behaviour).

Empty-page, corrupt-bytes, cancellation and media-type behaviour is unchanged and already tested.

### 5.2 The decoration guard (regression control)

`DecorationTextBlockClassifier` is a heuristic. Never let it remove body text:

- Never drop a block if it is the **only** text block on its page.
- Never drop a block whose text is longer than `PdfLayoutOptions.MaxDecorationCharacters` (default **200**).
- Never drop a block containing a sentence terminator followed by a space and a capital (two
  sentences are not a running head).
- `PdfLayoutOptions.StripDecoration` (default `true`) turns the whole step off.

### 5.3 Image emission (phase 2)

For each `IPdfImage image in page.GetImages()`:

- Skip if `image.WidthInSamples < PdfLayoutOptions.MinImageSamples` or height likewise (default **32**) — icons and rules.
- Bytes: prefer `image.TryGetPng(out var png)` → `Content = png`, `MediaType = "image/png"`; else
  if the image dictionary's `/Filter` is `DCTDecode` → `Content = image.RawBytes.ToArray()`,
  `MediaType = "image/jpeg"`; else skip and log at debug (`JPXDecode`, `CCITTFax`, `JBIG2` are out
  of scope — record the count in `Metadata` on the section for diagnostics).
- `Text` stays `null` (an undescribed image has no text — this is what keeps the chat path
  unchanged until phase 3, see 21.1).
- `PageNumber = n`; `Metadata`:
  `ElementMetadataKeys.BoundingBox` (from `image.Bounds`), `ElementMetadataKeys.ContentHash`
  (SHA-256 hex of `Content`), `ElementMetadataKeys.ImageOrdinal` (1-based per page),
  `ElementMetadataKeys.PixelWidth/PixelHeight`, `ElementMetadataKeys.IsInlineImage`.
- **Deduplicate by content hash within the document** (evidence #5): the second occurrence gets
  `Metadata[ElementMetadataKeys.DuplicateOf] = <first element's figure id>` and is not emitted as
  a separate image.

### 5.4 Text normalization

A static `PdfTextNormalizer` in the Pdf project, unit-tested on its own:

- Unicode NFKC on the ligature range U+FB00–U+FB06 only (`ﬁ` → `fi`), **not** on the whole
  string (NFKC on the whole string changes `²` to `2` and would corrupt `R²`).
- Collapse the spurious space Adobe emits after a ligature: regex `(?<=\p{L})\s(?=\p{Ll})` applied
  **only** when the preceding character was a ligature before normalization. Implement as: find
  ligature positions, normalize, then remove a single space immediately following each
  normalized ligature if the next character is a lowercase letter.
- Collapse runs of whitespace to one space; trim.
- Never touch digits, commas or periods (Hungarian decimals are `0,8858`).

### 5.5 Options and construction

```csharp
public sealed class PdfLayoutOptions
{
    public bool StripDecoration { get; set; } = true;
    public int MaxDecorationCharacters { get; set; } = 200;
    public bool EmitDecorationAsHeaderFooter { get; set; } = false;
    public int MinImageSamples { get; set; } = 32;
    public bool EmitImages { get; set; } = true;          // phase 2 flips the default from false to true
    public bool UseLayoutAnalysis { get; set; } = true;   // false = today's pdfPage.Text path, kept as the escape hatch
}
```

`PdfIngestionDocumentReader` gets a constructor `(IOptions<PdfLayoutOptions> options)` and keeps a
**parameterless constructor** that uses defaults, so the two existing test files compile
unchanged. Register options with `services.AddOptions<PdfLayoutOptions>()` in
`AddCoreAIPdfDocumentProcessing`. The reader is a singleton and must stay stateless per call.

`UseLayoutAnalysis = false` must reproduce today's output exactly; it is the operator's escape
hatch if layout analysis misbehaves on a corpus, and it is what the pinned tests compare against.

### 5.6 Tests (`Core/Documents/Services/PdfIngestionDocumentReaderTests.cs` — extend)

Fixtures come from a new test helper `tests/CrestApps.Core.Tests/Support/PdfFixtureBuilder.cs`
wrapping `PdfDocumentBuilder` (Appendix A.1 has the writer API). Every fixture is a few lines.

- `ReadAsync_TwoColumnPage_EmitsColumnsInReadingOrder` — left column text A then B, right column
  C then D, drawn so a raw content stream would interleave; assert element order A, B, C, D.
- `ReadAsync_RepeatedRunningHead_IsStrippedFromEveryPage` — five pages, same short line at the top
  of each plus body text; assert the head is absent and body present on every page.
- `ReadAsync_DecorationGuard_KeepsOnlyBlockOnPage` — a page whose only text is one short line;
  assert it survives.
- `ReadAsync_DecorationGuard_KeepsLongRepeatedBlock` — a 300-character paragraph repeated on three
  pages (boilerplate) survives.
- `ReadAsync_EveryElementHasTextAndPageNumber` — multi-page fixture; assert
  `document.EnumerateContent().All(e => e.PageNumber.HasValue && (e is IngestionDocumentImage || !string.IsNullOrWhiteSpace(e.Text)))`.
- `ReadAsync_LigatureWord_IsNormalized` — text containing `Elektroﬁ lterekkel` yields
  `Elektrofilterekkel`; and `R² = 0,8858` is preserved byte for byte.
- `ReadAsync_LayoutAnalysisDisabled_MatchesLegacyOutput` — same fixture through both modes; the
  legacy mode yields one paragraph per page whose text equals `pdfPage.Text.Trim()`.
- `ReadAsync_PageWithPngImage_EmitsImageElementWithHashAndBounds` (phase 2).
- `ReadAsync_SameImageTwice_SecondIsMarkedDuplicate` (phase 2).
- `ReadAsync_TinyImage_IsSkipped` (phase 2) — 8×8 PNG.
- `PdfTextNormalizer_*` — a `[Theory]` over ligature cases, whitespace, and the decimal-preservation case.

## 6. Caption resolution — `FigureCaptionProcessor`

`AIDocumentIngestionProcessor` in `src/Primitives/CrestApps.Core.AI.Documents/Ingestion/Processors/`.
Runs first. No model calls.

### 6.1 Inputs

Per page: the `IngestionDocumentImage` elements and the `IngestionDocumentParagraph` elements with
their `BoundingBox`, `ModalPointSize`, `ModalFontName` metadata (5.1). Page-level statistics
computed once per page: modal body point size, modal body font name, modal line height (median
block height / line count).

### 6.2 Caption candidates

A paragraph is a candidate when **either**:

1. It matches a configured caption pattern. Patterns are **data**: `CaptionPatternOptions.Patterns`
   is a list of `(Regex, Bucket)` with defaults for English (`^(Figure|Fig\.|Chart|Diagram|Table)\s*\d+`)
   and Hungarian (`^\d+\.\s*(ábra|táblázat)`), bucket = `figure` or `table`. Hosts add languages
   through `services.Configure<CaptionPatternOptions>`.
2. It is **typographically distinct**: `ModalPointSize` ≤ 0.9 × page modal body size, **or**
   `ModalFontName` differs from the page modal body font, **and** its length ≤
   `CaptionPatternOptions.MaxCaptionCharacters` (default 400).

### 6.3 Scoring

For each (image, candidate) pair on the same page compute `cost`:

```text
gap        = vertical distance between image bounds and candidate bounds, in modal line heights (0 when overlapping)
overlap    = 1 - horizontal overlap ratio (0 = candidate fully within image's x-range)
sameColumn = 0 when candidate's x-centre lies within image's x-range, else 1
blocked    = any non-candidate paragraph lies vertically between them  -> disqualify (cost = +inf)
prior      = 0 when candidate is on the learned side for its bucket, 0.5 when on the other side, 0.25 when unknown
cost       = gap + 2 * overlap + sameColumn + prior
```

Assign greedily by ascending cost with ceiling `CaptionPatternOptions.MaxAssignmentCost` (default
**6.0**); each image and each candidate is used at most once.

### 6.4 The learned prior (D4)

Before scoring, one pass over all pages: for each candidate that matched a **pattern** and has
exactly one image within 3 line heights, record `above` or `below` **per bucket**. Where a bucket
has ≥ `CaptionPatternOptions.MinSamplesForLearnedDirection` (default **5**) observations, the
majority becomes that bucket's prior; otherwise the configured default (`figure` = below, `table`
= above) is used.

### 6.5 Fallback and output

No caption assigned → look for an in-text reference: the first paragraph on the same page (then
±1 page) that matches the ordinal pattern for that bucket (`1\.\s*ábra`, `Figure 1`) with the
image's inferred ordinal; take that sentence as `Context`.

Write on the image element:

- `Metadata[FigureMetadataKeys.Caption]` — the caption text (or null)
- `Metadata[FigureMetadataKeys.CaptionSource]` — `pattern` | `typography` | `inTextReference` | `none`
- `Metadata[FigureMetadataKeys.Context]` — up to 600 characters of the nearest body paragraph(s)
- `Metadata[FigureMetadataKeys.Bucket]` — `figure` | `table` | `unknown`
- `Metadata[FigureMetadataKeys.Ordinal]` — parsed from the caption when present

Mark the caption paragraph itself with `Metadata[ElementMetadataKeys.IsCaptionFor] = <image id>`
so flattening can avoid emitting it twice (section 9).

### 6.6 Pluggability

Two interfaces, default implementations registered with `TryAddSingleton`:

- `IFigureCaptionCandidateDetector.GetCandidates(page context) → IReadOnlyList<CaptionCandidate>`
- `IFigureCaptionResolver.Resolve(images, candidates, prior) → IReadOnlyList<CaptionAssignment>`

A host with an unusual layout replaces one without forking the processor.

### 6.7 Tests (`Core/Documents/Ingestion/FigureCaptionProcessorTests.cs`)

These tests build `IngestionDocument`s **directly** (paragraphs and images with metadata), not
PDFs — the processor never sees a PDF.

- `Process_CaptionBelowFigure_IsAssigned`
- `Process_CaptionAboveTable_IsAssignedUsingTableBucket`
- `Process_BodyTextBetweenImageAndCandidate_Disqualifies`
- `Process_LearnedDirection_ConvergesWithFiveSamples` — five confident below-captions, one
  ambiguous case equidistant above and below; below wins.
- `Process_LearnedDirection_FallsBackBelowFiveSamples` — four samples; configured default wins.
- `Process_TwoBucketsLearnIndependently` — figures below, tables above in the same document.
- `Process_NoCaption_UsesInTextReference`
- `Process_TypographicallyDistinctUnnumberedCaption_IsCandidate`
- `Process_CaptionParagraph_IsMarkedIsCaptionFor`
- `Process_HostPattern_IsHonoured` — a configured German pattern (`Abbildung 3`).

## 7. Salience scoring — `FigureSalienceProcessor`

Runs after captions. No model calls. Decides which images deserve storage and which deserve vision.

### 7.1 Signals

| Signal | How computed | Effect |
|---|---|---|
| Has resolved caption | `FigureMetadataKeys.Caption` non-null | +3 |
| Cited by ordinal in body text | any paragraph in the document matches the bucket pattern with the same ordinal | +2 |
| **Distinct quantized colour count** | decode PNG (see 7.3), quantize to 4 bits/channel, count distinct values on a ≤ 64×64 downsample | ≤ 64 colours: +2 (chart/diagram); ≥ 2000: −2 (photo) |
| Long axis-aligned runs | on the same downsample, count rows/columns where ≥ 60 % of pixels share one colour | ≥ 4 runs: +1 |
| Repeats across pages | same `ContentHash` on ≥ 3 pages | −4 (logo, ad furniture) |
| Small or extreme aspect | `< 150×150` samples or aspect > 6:1 | −2 |
| Full-bleed with little text | image covers ≥ 80 % of page area and page text < 200 chars | −3 (advert, evidence #8) |

Score starts at 0. Tiers, written to `Metadata[FigureMetadataKeys.Tier]`:

- **Skip** — score < `FigureSalienceOptions.CaptionOnlyThreshold` (default **0**): the element is
  removed from the document. No bytes stored.
- **CaptionOnly** — below `DescribeThreshold` (default **3**): stored, caption + context indexed,
  no vision call.
- **Describe** — at or above: vision transcription in phase 3.

`FigureProcessingMode` from the context overrides: `Off` → every image becomes `Skip`; `All` →
every non-`Skip` image becomes `Describe` (repeats and tiny images still skip).

### 7.2 Budget

If more than `context.MaxFigureDescriptionsPerDocument` images land in `Describe`, demote the
lowest-scoring surplus to `CaptionOnly`. Never drop.

### 7.3 Decoding without a graphics dependency

The repository has no image library. Decode PNG with a small internal `PngSampler` supporting
8-bit RGB/RGBA/greyscale, non-interlaced, filters 0–4 (`System.IO.Compression.ZLibStream`) —
about 150 lines, fully unit-tested. Anything else (JPEG, 16-bit, palette, interlaced) returns
`null` and the colour/edge signals are simply not applied. That keeps the dependency footprint at
zero and never blocks ingest.

### 7.4 Tests (`Core/Documents/Ingestion/FigureSalienceProcessorTests.cs`)

Synthetic PNGs from `tests/CrestApps.Core.Tests/Support/TestImageFactory.cs`
(`CreatePng(width, height, Func<int,int,(byte,byte,byte)> pixel)` — a minimal encoder: zlib via
`ZLibStream`, CRC32 by hand).

- `Process_FewColoursWithGridlines_ScoresDescribe` — 6-colour bar chart pattern.
- `Process_NoisyGradient_ScoresSkip` — pseudo-random RGB noise (photo proxy), no caption.
- `Process_RepeatedHashOnThreePages_ScoresSkip` — even with a caption.
- `Process_TinyImage_IsSkipped`
- `Process_ModeOff_SkipsEverything`
- `Process_ModeAll_PromotesCaptionOnlyToDescribe_ButNotRepeats`
- `Process_Budget_DemotesLowestToCaptionOnly` — budget 2, three Describe candidates.
- `PngSampler_DecodesEachFilterType` — a `[Theory]` over filter bytes 0–4.
- `PngSampler_UnsupportedBitDepth_ReturnsNull`

## 8. Figure description — `FigureDescriptionProcessor` (D2, D7, D17)

Runs last. The only processor that calls a model.

### 8.1 Gates, in order

1. `context.FigureMode == Off` → return.
2. No image with `Tier == Describe` → return.
3. **Capability.** Resolve the deployment:
   `context.VisionDeploymentName` → `IAIDeploymentManager.FindByNameAsync`; else
   `ResolveSlotAsync(AIDeploymentSlotNames.Vision)`. Then **verify**
   `deployment.TryGet<AIDeploymentMetadata>(out var m) && m.SupportsFeature(AIDeploymentFeatureNames.ImageInput)`
   (the exact check `DefaultImageAnalysisService.ResolveVisionDeploymentAsync` already uses).
   `ResolveSlotAsync`'s documented tail is "the first deployment capable of the terminal slot's
   required feature", so non-null is **not** proof of vision. If the check fails: log once at
   Information, return. **Never throw.**
4. `context.DescribeFiguresInline == false` → do nothing here; the indexer path backfills
   (section 12.4). Return.

### 8.2 The call

Extend `IImageAnalysisService` (D17) with an overload taking a request object; keep the existing
overload delegating to it:

```csharp
public sealed class ImageAnalysisRequest
{
    public ReadOnlyMemory<byte> Content { get; init; }
    public string ContentType { get; init; }
    public string FileName { get; init; }
    public string Caption { get; init; }          // new
    public string Context { get; init; }          // new — surrounding paragraph text
    public string Language { get; init; }         // new — transcribe in this language, never translate
    public string TemplateId { get; init; }       // default AITemplateIds.ImageAnalysis
    public string DeploymentName { get; init; }   // explicit deployment (D13), else slot resolution
}

Task<ImageAnalysisResult> AnalyzeAsync(ImageAnalysisRequest request, CancellationToken cancellationToken = default);
```

`DefaultImageAnalysisService` renders `TemplateId` (new template
`src/Primitives/CrestApps.Core.AI/Templates/Prompts/figure-transcription.md`, id
`AITemplateIds.FigureTranscription = "figure-transcription"`), appends a user message with the
caption and context as text plus the image as `DataContent`, and parses the same JSON shape
(`caption`, `description`, `ocr_text`, `detected_entities`) the existing template uses — so
`ImageAnalysisResult` is reused unchanged.

The **figure-transcription prompt** asks for a *dense literal transcription*: chart type; axis
titles and units; every tick label; series and legend entries; every printed number and
equation verbatim (`y = 1,0892x`, `R² = 0,8858`); the trend in one sentence; for a diagram, every
label and the relationships; **in the document's language**; no interpretation, no rounding, no
values that are not printed (D9). Cap the description at ~350 tokens by instruction.

### 8.3 Output

`image.AlternativeText = description + "\n" + ocr_text` (whichever parts are non-empty).
`Metadata[FigureMetadataKeys.DescriptionSource] = "vision"`,
`Metadata[FigureMetadataKeys.DescriptionModel] = deployment.Name`,
`Metadata[FigureMetadataKeys.DescriptionPromptVersion] = FigureTranscriptionPromptVersion` (a
constant bumped whenever the template changes — it is part of the cache key).

### 8.4 Cache

`IFigureDescriptionCache` with `TryGetAsync(contentHash, promptVersion)` / `SetAsync(...)`.
Default implementation: `IMemoryCache`-backed, process-lifetime, keyed
`{contentHash}:{promptVersion}`. Phase 4 adds a store-backed implementation that consults the
`KnowledgeObject` store (`FindFigureByContentHashAsync`), so a re-ingested file pays nothing.

### 8.5 Failure posture

Every exception from the model call is caught per image, logged at Warning with the file name and
page, and the image is demoted to `CaptionOnly`. A `CancellationToken` cancellation propagates.
`MaxFigureDescriptionsPerDocument` is enforced again here as a hard stop on calls made.

### 8.6 Tests (`Core/Documents/Ingestion/FigureDescriptionProcessorTests.cs`, Moq)

- `Process_NoVisionDeployment_ReturnsTextOnlyAndLogsOnce` — `IAIDeploymentManager` returns null;
  no `IImageAnalysisService` call; a captured `ILogger` sees exactly one Information entry.
- `Process_DeploymentWithoutImageInput_TreatedAsUnavailable` — deployment resolved, metadata lacks
  `imageInput`; no call.
- `Process_ModeOff_NoCalls`
- `Process_InlineFalse_NoCalls` (indexer path)
- `Process_DescribeTier_CallsAnalyzeWithCaptionContextAndLanguage` — assert the request object.
- `Process_CaptionOnlyTier_NoCall`
- `Process_SameHashTwice_OneCall` — two images, same bytes; second is served from cache.
- `Process_PromptVersionChange_MissesCache`
- `Process_AnalyzeThrows_DemotesToCaptionOnlyAndContinues` — three images, middle one throws; the
  other two are described.
- `Process_BudgetExceeded_StopsCalling`
- `DefaultImageAnalysisService_RequestOverload_RendersFigureTemplateAndSendsImage` — Moq
  `IChatClient`; assert the messages contain the caption text and one `DataContent` with the
  right media type; assert `ImageAnalysisResult` parsed.

## 9. Flattening — `GetSemanticText` and the chat-path shape (D14)

### 9.1 `IngestionDocumentElementExtensions.GetSemanticText(this IngestionDocumentElement)`

`src/Primitives/CrestApps.Core.AI.Documents/Ingestion/IngestionDocumentElementExtensions.cs`:

| Element | Returns |
|---|---|
| `IngestionDocumentImage` with `AlternativeText` | the alt text |
| `IngestionDocumentImage` without alt but with `FigureMetadataKeys.Caption` | the caption |
| `IngestionDocumentImage` otherwise | `null` |
| `IngestionDocumentTable` | `Text` if set; else a markdown rendering of `Cells` (row per line, cells joined by ` \| `) |
| anything else | `Text` |

Also `GetFigureId(this IngestionDocumentImage)` (reads `FigureMetadataKeys.Id`) and
`GetPage(this IngestionDocumentElement)`.

### 9.2 Flattening in `DefaultAIDocumentProcessingService`

Replace `.Select(element => element.Text)` with a `FlattenForEmbedding(IngestionDocument)` helper:

- Skip elements marked `ElementMetadataKeys.IsCaptionFor` (the caption is emitted with its figure).
- For an image with semantic text, emit a fenced block so a chunk can never lose the association:

```text
[figure {figureId} | page {page} | {caption}]
{alternative text}
[/figure]
```

- Otherwise emit `GetSemanticText()` when non-empty.
- Join with `'\n'` exactly as today.

Chunking is 500 tokens / 50 overlap. The description is capped at ~350 tokens (8.2) so a figure
block survives as one chunk. **If a block is split anyway, repeat the `[figure …]` header line at
the start of the continuation chunk** — implement as a post-pass over `NormalizeDocumentChunksAsync`
output: a chunk that contains `[/figure]` without a preceding `[figure` gets the header of the
previous chunk's open figure prepended.

The chat path stores figure bytes so a later tool can show them: for each stored figure,
`IDocumentFileStore.SaveFileAsync(DocumentFileStoragePath.Create(referenceType, referenceId, $"{figureId}.png", "figures").StoragePath, stream)`,
and the list `{ figureId, page, caption, storagePath, mediaType }` is put on the `AIDocument` as
`document.Put(new DocumentFigureList { … })` (`ExtensibleEntity.Properties`). No new store.

### 9.3 Tests

- `GetSemanticText_Paragraph_ReturnsText`, `_ImageWithAlt_ReturnsAlt`, `_ImageWithCaptionOnly_ReturnsCaption`,
  `_BareImage_ReturnsNull`, `_TableWithoutText_RendersCells`.
- `ProcessFileAsync_PlainTextPdf_ProducesSameWordsAsBefore` — the pinned regression (21.5) —
  vocabulary set equality against `UseLayoutAnalysis = false` output.
- `ProcessFileAsync_DescribedFigure_FigureBlockSurvivesAsOneChunk`
- `ProcessFileAsync_SplitFigureBlock_ContinuationRepeatsHeader` — force with a tiny chunk size
  through a test `IAITextNormalizer`.
- `ProcessFileAsync_CaptionParagraph_NotEmittedTwice`
- `ProcessFileAsync_StoresFigureBytesAndRecordsList` — fake `IDocumentFileStore`.

## 10. Metadata keys (single source of truth)

`src/Primitives/CrestApps.Core.AI.Documents/Ingestion/ElementMetadataKeys.cs` and `FigureMetadataKeys.cs`
— `public static class` of `const string` keys, all prefixed `crestapps.`:

```text
ElementMetadataKeys:  BoundingBox  ModalPointSize  ModalFontName  IsDecoration  IsCaptionFor  Folio  SectionLabel  ArticleOrdinal
FigureMetadataKeys:   Id  ContentHash  ImageOrdinal  PixelWidth  PixelHeight  IsInlineImage  DuplicateOf  Caption  CaptionSource
                      Context  Bucket  Ordinal  Tier  SalienceScore  DescriptionSource  DescriptionModel  DescriptionPromptVersion
                      ValueConfidence  StoragePath
```

Figure id shape: `{documentIdentifier}-p{page}-{imageOrdinal}` — stable across re-reads of the
same file. Every processor reads and writes through these constants; no string literals.

---

# Part 2 — Typed knowledge in the data source, and exposure

Part 2 makes figures, charts, tables and text **separate rows** of the knowledge base, gives them
a persistent home so they can be fetched back by id, and exposes them over MCP.

## 11. The knowledge object model (D8, D15)

### 11.1 Hierarchy and canonical ids

```text
document   doc:{fileKey}                                   the ingested file: title, language, pages, hash
  article  article:{fileKey}:{a}                           title, authors, type, pageStart, pageEnd, abstract   (phase 7; until then exactly one)
    text   text:{fileKey}:{a}:{n}                          one embedded chunk of article text (≤ 500 tokens)
    figure figure:{fileKey}:{a}:{n}                        caption + description; bytes in IDocumentFileStore
    chart  chart:{fileKey}:{a}:{n}                         a figure whose description is a chart, plus optional exact series (D9)
    table  table:{fileKey}:{a}:{n}                         caption + columns + rows
```

`fileKey` is the first 16 hex characters of the file's SHA-256 — so re-ingesting identical bytes
produces identical ids, and two indexers ingesting the same file into the same data source
collide on purpose. Nothing in the id scheme says "magazine"; a manual, a datasheet or a contract
uses the same shape with one article.

Every object carries `rootId` (the `doc:` id) and `parentId`, so a hit on a `text` row can be
widened to its article, and a `figure` hit can be cited as *"{article title}, p. {folio}, {caption}"*.

### 11.2 The entity

One catalog entry (D15), in `src/Abstractions/CrestApps.Core.AI.Abstractions/Models/KnowledgeObject.cs`:

```csharp
/// One typed knowledge object produced by ingestion. Source holds the owning AI data source id
/// (the same convention WebCrawlState uses for its owning crawler).
public sealed class KnowledgeObject : SourceCatalogEntry, IModifiedUtcAwareModel, ICloneable<KnowledgeObject>
{
    public string CanonicalId { get; set; }        // 11.1 — unique within the data source
    public string ObjectType { get; set; }         // KnowledgeObjectTypes.Document | Article | Text | Figure | Chart | Table
    public string RootId { get; set; }
    public string ParentId { get; set; }
    public string IndexerId { get; set; }          // null for manual uploads
    public string SourceItemId { get; set; }       // connector item id (URL, blob name, path); null for uploads
    public string Title { get; set; }
    public string Content { get; set; }            // what gets embedded (11.3)
    public string Language { get; set; }
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string Folio { get; set; }              // printed page label when known (phase 7)
    public int Ordinal { get; set; }
    public string ContentHash { get; set; }        // SHA-256 hex of the bytes (figure) or of Content (others)
    public string MediaType { get; set; }          // figures only
    public string StoragePath { get; set; }        // figures only — IDocumentFileStore path
    public string Status { get; set; }             // KnowledgeObjectStatus.Ready | PendingDescription | Failed
    public DateTime CreatedUtc { get; set; }
    public DateTime? ModifiedUtc { get; set; }
    // type-specific detail lives in Properties: FigureDetails, ChartDetails, TableDetails, ArticleDetails (Put<T>/TryGet<T>)
}
```

`FigureDetails { Caption, CaptionSource, Context, Tier, Description, DescriptionModel, DescriptionPromptVersion, PixelWidth, PixelHeight }`,
`ChartDetails { ChartType, ValueConfidence (Exact|AxesOnly|Descriptive), Series: [{ Name, Points: [[x,y]] }], AxisX, AxisY }`,
`TableDetails { Caption, Columns: [string], Rows: [[string]] }`,
`ArticleDetails { Authors: [string], SectionLabel, Abstract }`.

**Store** — `IKnowledgeObjectStore : ISourceCatalog<KnowledgeObject>` in
`src/Abstractions/CrestApps.Core.AI.Abstractions/DataSources/IKnowledgeObjectStore.cs`:

```csharp
Task<KnowledgeObject> FindByCanonicalIdAsync(string dataSourceId, string canonicalId, CancellationToken ct = default);
Task<IReadOnlyCollection<KnowledgeObject>> GetByCanonicalIdsAsync(string dataSourceId, IEnumerable<string> canonicalIds, CancellationToken ct = default);
Task<IReadOnlyCollection<KnowledgeObject>> GetByRootIdAsync(string dataSourceId, string rootId, CancellationToken ct = default);
Task<IReadOnlyCollection<KnowledgeObject>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken ct = default);   // paged overload too
Task<IReadOnlyCollection<KnowledgeObject>> GetByStatusAsync(string dataSourceId, string status, int take, CancellationToken ct = default);
Task<KnowledgeObject> FindFigureByContentHashAsync(string contentHash, string promptVersion, CancellationToken ct = default);   // description cache (8.4)
Task DeleteByRootIdAsync(string dataSourceId, string rootId, CancellationToken ct = default);
```

Implement per **Appendix B** (EntityCore + YesSql + handler + factory + DI). The `ValidatingAsync`
of `KnowledgeObjectCatalogHandler` requires `CanonicalId`, `ObjectType`, `RootId`, `Source`.

### 11.3 What is embedded, per type

| Type | `Content` (embedded) | Kept structured |
|---|---|---|
| document | title + first 1,000 characters of body | page count, language, hash |
| article | title + authors + section label + abstract/first paragraph + headings | `ArticleDetails` |
| text | the chunk text, prefixed on the first chunk with the article title | — |
| figure | caption + context (CaptionOnly) or caption + vision description + OCR (Describe) | `FigureDetails`; bytes in file store |
| chart | as figure, plus for `Exact` series a compact `name: (x,y) (x,y)…` rendering | `ChartDetails` |
| table | caption + header row + every row serialized `col=value; col=value` | `TableDetails` |

`Content` is produced **already within the 500-token chunk budget** by the ingestion service
(11.5), so the indexing service never re-chunks a knowledge row (13.3).

### 11.4 Charts — the correctness hazard (D9)

Do **not** ask a vision model for `data: [[-50, 820], …]`. On the reference document's p4 scatter
there are ~1,500 overlapping points; on p8's stacked bars, reading 24 categories × 6 series off a
raster to two significant figures is guesswork. A fabricated series stored as data answers *"what
tensile strength at 150 °C?"* with a confident, specific, wrong number.

| Source | Method | `ValueConfidence` |
|---|---|---|
| Vector chart — geometry in the content stream | `page.ExperimentalAccess.Paths`, axis transform from tick labels (phase 8) | `Exact` |
| Printed data labels on bars/points | text extraction, associated by position (phase 8) | `Exact` |
| Axis ticks and legend only | text extraction | `AxesOnly` |
| Raster chart, no labels | vision transcription of the *shape and trend*, prose only (phase 3) | `Descriptive` |

Retrieval (14) renders a `Descriptive` chart with its trend statement, its link, and the sentence
*"values are not machine-readable"*. There is no estimate tier (22.6).

### 11.5 Producing objects from an `IngestionDocument` — `KnowledgeObjectBuilder`

Pure, stateless, unit-tested class in `CrestApps.Core.AI.Documents/Knowledge/`:

```csharp
IReadOnlyList<KnowledgeObject> Build(IngestionDocument document, KnowledgeObjectBuildOptions options, IReadOnlyList<string> chunkedArticleText);
```

Steps: (1) `document` object from `Identifier`, hash, page count; (2) one `article` (phase 7
replaces this with `IDocumentStructureAnalyzer`); (3) article text = `GetSemanticText()` of every
non-image, non-caption, non-decoration element, in order → chunk with
`IAITextNormalizer.NormalizeAndChunkAsync` → `text` objects, `Ordinal` = chunk index,
`PageStart/PageEnd` = pages of the first/last element in the chunk (track element→chunk by
character offsets); (4) `figure` / `chart` per stored `IngestionDocumentImage` (Tier ≠ Skip),
`chart` when the description or caption bucket says chart, `Status = PendingDescription` when
Tier = Describe and no `AlternativeText` yet; (5) `table` per `IngestionDocumentTable`.

**Tests** (`Core/Documents/Knowledge/KnowledgeObjectBuilderTests.cs`): ids are stable for the same
bytes; every object has `RootId`/`ParentId`; text chunks carry the right page range; a
`CaptionOnly` figure has `Status = Ready`; a `Describe` figure without alt text is
`PendingDescription`; a chart with `Exact` series serializes them into `Content`; table content
includes every cell.

## 12. The `Ingested` source type and the ingestion service (D20)

### 12.1 Source type

`AIDataSourceSourceTypes.Ingested = "Ingested"`. Register in `CrestApps.Core.AI.Documents`
(`AddCoreAIDocumentProcessing`):

```csharp
services.TryAddKeyedScoped<IAIDataSourceSourceHandler, IngestedAIDataSourceSourceHandler>(AIDataSourceSourceTypes.Ingested);
services.TryAddKeyedSingleton<IAIReferenceLinkResolver, IngestedReferenceLinkResolver>(AIDataSourceSourceTypes.Ingested);
services.Configure<AIDataSourceSourceOptions>(o => o.AddOrUpdate(AIDataSourceSourceTypes.Ingested,
    new LocalizedString("Ingested", "Ingested Files"),
    new LocalizedString("Ingested Description", "A knowledge base fed by uploaded files and indexers. Text, figures, charts and tables are stored as separate searchable objects.")));
```

The `Web` type and `WebAIDataSourceSourceHandler` are **not modified**.

### 12.2 `IngestedAIDataSourceSourceHandler`

Mirrors `WebAIDataSourceSourceHandler` (copy its shape):

- `SourceType => Ingested`; `GetReferenceTypeAsync => Ingested`.
- `ValidateAsync`: nothing to validate (target bucket).
- `ReadAsync(dataSource)`: `store.GetByDataSourceIdAsync` (paged) → one `SourceDocument` per object:
  key = `CanonicalId`; `Title`, `Content` from the object; `IsPreChunked = true` (13.3);
  `Fields` = `{ contentType, rootId, parentId, page = PageStart, folio, indexerId, language, imageUri (figures/charts), tier, valueConfidence }`.
- `ReadByIdsAsync(dataSource, ids)`: `GetByCanonicalIdsAsync` → same mapping.

`IngestedReferenceLinkResolver.ResolveLink(referenceId, metadata)`: for `figure:`/`chart:` ids
returns the figure download route (`/ai/knowledge/{dataSourceId}/figures/{canonicalId}` — an
endpoint added to `CrestApps.Core.AI.Documents/Endpoints`, authorized like the existing document
download endpoints); otherwise `null`.

### 12.3 `IKnowledgeIngestionService`

The one entry point both manual upload and indexers call:

```csharp
Task<KnowledgeIngestionResult> IngestAsync(
    AIDataSource dataSource, Stream content, string fileName, string mediaType,
    KnowledgeIngestionOptions options,   // FigureMode, deployment names, budget, IndexerId, SourceItemId, ChangeToken
    CancellationToken ct);
Task RemoveAsync(AIDataSource dataSource, string rootId, CancellationToken ct);
```

`IngestAsync`: hash the stream (buffer to `MemoryStream` when not seekable; the reader does this
already) → `DocumentIngestionContext { DescribeFiguresInline = false, … }` →
`IAIDocumentIngestionPipeline.IngestAsync` → store figure bytes
(`DocumentFileStoragePath.Create(AIDataSourceSourceTypes.Ingested, dataSource.ItemId, $"{figureId}.png", "figures")`)
→ `KnowledgeObjectBuilder.Build` → delete existing objects with the same `RootId` (re-ingest) →
`store.CreateAsync` each → `IAIDataSourceIndexingQueue.QueueSyncDataSourceDocumentsAsync(dataSource.ItemId, canonicalIds)`.
Returns counts and the root id.

`RemoveAsync`: `store.DeleteByRootIdAsync` → delete figure files → `QueueRemoveDataSourceDocumentsAsync`.

### 12.4 Figure description backfill (D7)

`FigureDescriptionBackfillService` — a `BackgroundService` in `CrestApps.Core.AI.Documents`,
registered with `TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, …>())`:

- Every `KnowledgeIngestionOptions.BackfillIntervalSeconds` (default 30): for each data source of
  type `Ingested`, `store.GetByStatusAsync(dataSourceId, PendingDescription, take: 20)`.
- For each: load bytes from the file store, resolve the deployment (explicit from the owning
  indexer's `VisionDeploymentId` when `IndexerId` is set, else slot) and call
  `IImageAnalysisService.AnalyzeAsync(request)` exactly as 8.2. Update `FigureDetails.Description`,
  `Content` (11.3), `Status = Ready`; on failure `Status = Failed` with the error in
  `FigureDetails.Error` — never retried automatically (an operator "Reset" re-queues).
- Re-queue the object: `QueueSyncDataSourceDocumentsAsync(dataSourceId, [canonicalId])`.
- Cache: before calling the model, `FindFigureByContentHashAsync(hash, promptVersion)` → copy the
  description (this is the store-backed `IFigureDescriptionCache` of 8.4).

Concurrency: one data source at a time, `MaxConcurrentVisionCalls` (default 2) within it.

### 12.5 Manual intake (so phase 4 is useful before indexers exist)

An admin action **"Upload files"** on an `Ingested` data source (MVC area `DataSources` and the
Blazor page) that accepts multiple files and calls `IKnowledgeIngestionService.IngestAsync` per
file with the data source's default options. This is the smallest possible UI: a form, a loop, a
summary of counts. It reuses `ChatDocumentsOptions.AllowedFileExtensions` for the accept list.

### 12.6 Tests

- `IngestedHandler_ReadAsync_YieldsOnePreChunkedRowPerObjectWithTypedFields` (Moq store).
- `IngestedHandler_ReadByIdsAsync_MapsOnlyRequestedIds`.
- `KnowledgeIngestionService_IngestAsync_StoresObjectsFilesAndQueuesSync` — fake
  `IDocumentFileStore`, Moq queue; assert the queued ids equal the stored canonical ids.
- `KnowledgeIngestionService_ReingestSameBytes_ReplacesObjectsUnderSameRoot`.
- `KnowledgeIngestionService_RemoveAsync_DeletesObjectsFilesAndQueuesRemove`.
- `Backfill_PendingFigure_DescribedAndRequeued`; `Backfill_AnalyzeThrows_MarksFailedNotRetried`;
  `Backfill_CacheHitByHash_NoModelCall`; `Backfill_UsesIndexerVisionDeploymentWhenSet`.
- `IngestedReferenceLinkResolver_FigureIdYieldsRoute_TextIdYieldsNull`.

## 13. Index schema and indexing-service changes (D16, D21)

### 13.1 Columns

Add to `DataSourceConstants.ColumnNames`: `ContentType = "contentType"`, `RootId = "rootId"`,
`ParentId = "parentId"`, `Page = "page"`. Add to `DataSourceSearchIndexProfileHandler.BuildFields`:
three `Keyword` fields with `IsFilterable = true` and one `Integer` with `IsFilterable = true`.

`DefaultAIDataSourceIndexingService.IndexDocumentsAsync` writes them when present in
`SourceDocument.Fields` (read by the constant keys, then removed from the `filters` copy so they
are not stored twice). Rows from every other source type carry `contentType = "text"` explicitly
from now on; rows written before this change have no value and **every reader treats null as
`text`**.

### 13.2 Schema upgrade for existing indexes

Add to `ISearchIndexManager` a **default-implemented** member so external providers keep compiling:

```csharp
Task<bool> TryAddFieldsAsync(IIndexProfileInfo profile, IReadOnlyCollection<SearchIndexField> fields, CancellationToken cancellationToken = default)
    => Task.FromResult(false);
```

Implement in the three providers: PostgreSQL `ALTER TABLE … ADD COLUMN IF NOT EXISTS` per field
(the pattern of `EnsureDataSourceFilterColumnAsync`); Elasticsearch `PutMapping` with explicit
`keyword` / `integer` types (do not rely on dynamic `text` mapping — filters need `keyword`);
Azure AI Search `GetIndex` → append missing `SearchField`s → `CreateOrUpdateIndex` (allowed for
added non-key fields). `EnsureKnowledgeBaseIndexAsync` calls it when the index already exists and
logs at Information when a provider returns `false` ("typed filters unavailable on this index
until it is recreated").

### 13.3 Pre-chunked rows

`SourceDocument` (Infrastructure.Abstractions) gains `public bool IsPreChunked { get; set; }`.
In `IndexDocumentsAsync`: when `IsPreChunked`, `chunkTexts = [await _textNormalizer.NormalizeContentAsync(content)]`
(normalize, never split) and the title is **not** prepended (the builder already did, 11.3).
Everything else — embedding, `chunkId = {referenceId}_0`, batching — is unchanged.

### 13.4 Delete by reference id

Add to `IDataSourceContentManager`, default-implemented:

```csharp
Task<bool> DeleteByReferenceIdsAsync(IIndexProfileInfo profile, string dataSourceId, IReadOnlyCollection<string> referenceIds, CancellationToken ct = default)
    => Task.FromResult(false);
```

PostgreSQL: `DELETE … WHERE dataSourceId = @ds AND referenceId = ANY(@ids)`. Elasticsearch:
`DeleteByQuery` with `term dataSourceId` + `terms referenceId`. Azure: search `filter = dataSourceId eq '…' and search.in(referenceId, '…', '|')`
selecting `chunkId`, then `ISearchDocumentManager.DeleteAsync(ids)` — exactly how
`DeleteByDataSourceIdAsync` already works there; batch `search.in` lists at 100 ids.
`DefaultAIDataSourceIndexingService` calls it in the three places that call `BuildChunkIds`
today and falls back to `BuildChunkIds` only when it returns `false`.

### 13.5 `DataSourceSearchResult`

Add `ContentType`, `RootId`, `ParentId`, `Page` (`int?`) and `Filters`
(`IReadOnlyDictionary<string, object>`, may be empty). Each provider's `SearchAsync` adds the four
columns to its `Select`, reads them null-safely, and fills `Filters` from the `filters` column
where the provider stores it (PostgreSQL JSONB → dictionary; Elasticsearch object → dictionary;
Azure → empty unless 21.3's investigation shows it is stored). `DataSourceSearchResultSelector.FuseTopResults`
is unchanged.

### 13.6 Tests

`tests/CrestApps.Core.Tests/Core/Indexing/DefaultAIDataSourceIndexingServiceTests.cs` (new; Moq
for `IAIDataSourceStore`, `ISearchIndexProfileManager`, `IAIDeploymentManager`, `IAIClientFactory`,
a recording `ISearchDocumentManager`, a fake `IEmbeddingGenerator` that returns a fixed vector,
a fake `IAITextNormalizer` that splits on `\n\n`):

- `IndexDocuments_PlainSource_ChunksAndWritesContentTypeText`
- `IndexDocuments_PreChunkedSource_WritesExactlyOneRowAndDoesNotPrependTitle`
- `IndexDocuments_TypedFields_LandInColumnsAndNotInFilters`
- `IndexDocuments_ExistingIndex_CallsTryAddFields`
- `Remove_ProviderSupportsReferenceDelete_DoesNotEnumerateChunkIds`
- `Remove_ProviderReturnsFalse_FallsBackToChunkIdEnumeration`
- `BuildFields_ContainsTypedColumnsAsFilterableKeywords`
- Per provider: a unit test of the pure filter/SQL string builder each provider adds
  (`DataSourceAzureAISearchDocumentIdFilterBuilder` is the precedent for Azure).

## 14. Retrieval — typed, navigable

`DataSourceRetrieval` (static, `SearchAsync` returns `string`) gains `SearchDetailedAsync`
returning `DataSourceRetrievalResult`; `SearchAsync` becomes `(await SearchDetailedAsync(...)).Text`
so every existing caller is untouched.

```csharp
public sealed class DataSourceRetrievalResult
{
    public string Text { get; init; }                              // exactly what SearchAsync returns today, plus the new blocks
    public IReadOnlyList<RetrievedFigure> Figures { get; init; }   // Id, Title, Caption, Uri, MediaType, ValueConfidence, Page
    public IReadOnlyList<RetrievedTable> Tables { get; init; }
    public override string ToString() => Text;                     // so chat tool invocation keeps working unchanged
}
```

Rendering, appended after today's `Relevant content …` block and before `References:`:

```text
Figures:
[fig:1] 3. ábra — Stacked bar chart … (p. 8) crestapps://datasource/{dsId}/figure/{canonicalId}   values: descriptive — not machine-readable
Tables:
[tbl:1] 2. táblázat — columns: Anyag, Rm (MPa), T (°C) (p. 5)
```

Grouping: hits are grouped by `RootId` then `ParentId` so a figure and the paragraph that cites it
render adjacently; `ReferenceCollector.Track` is called with the **article/document title** rather
than the chunk's own title so citations read *"{title}, p. {page}"*. `DataSourceSearchToolSettings`
gains `ContentTypes` (`string[]`, null = all) which becomes `contentType eq …` OR-clauses appended
to the provider filter through the existing `IODataFilterTranslator`.

**Tests** (`Core/Services/DataSourceRetrievalTests.cs`, extending the Moq setup in
`DataSourceSearchToolInstanceTests`): `SearchAsync_TextUnchangedForTextOnlyResults` (the pinned
one — byte-identical to the pre-change rendering for a text-only result set);
`SearchDetailed_FigureHit_RendersFiguresBlockAndPopulatesFigures`;
`SearchDetailed_DescriptiveChart_StatesValuesNotMachineReadable`;
`SearchDetailed_ContentTypesFilter_IsTranslated`; `SearchDetailed_GroupsByRootThenParent`.

## 15. MCP exposure (D3)

### 15.1 Prerequisite — stop flattening tool results

`McpToolResultMapper.ToCallToolResult(object result)` in `CrestApps.Core.AI.Mcp`:

| `result` is | Content |
|---|---|
| `null` | one empty `TextContentBlock` |
| `string` | one `TextContentBlock` |
| `IMcpToolContentProvider` (new interface: `IReadOnlyList<ContentBlock> ToContentBlocks()`) | its blocks |
| `AIContent` / `IEnumerable<AIContent>` | mapped: `TextContent` → text; `DataContent` image → `ImageContentBlock` (base64); `UriContent` → `ResourceLinkBlock` |
| anything else | `ToString()` |

Replace the three call sites in `McpServerBuilderExtensions` with the mapper. `DataSourceRetrievalResult`
implements `IMcpToolContentProvider`: one `TextContentBlock(Text)` plus one `ResourceLinkBlock`
per figure (`Uri`, `Name` = caption, `MimeType`). **Tests** in `McpServerBuilderExtensionsTests`
(the existing file has the harness): a tool returning `DataSourceRetrievalResult` with one figure
yields two blocks; a tool returning a string yields one text block (pinned).

### 15.2 Resource template and handler

- Constants `DataSourceFigureResourceConstants.Type = "datasource-figure"`, URI template
  `crestapps://datasource/{dataSourceId}/figure/{figureId}`.
- `DataSourceFigureResourceHandler : McpResourceTypeHandlerBase` (copy `FtpResourceTypeHandler`'s
  shape): `figureId = SanitizePath(variables["figureId"])`; load
  `IKnowledgeObjectStore.FindByCanonicalIdAsync(dataSourceId, figureId)`; **refuse unless
  `object.Source == dataSourceId` and `ObjectType` is figure/chart** (ids are guessable — 22.8);
  read bytes from `IDocumentFileStore`; return `BlobResourceContents { Uri, MimeType, Blob = base64 }`;
  errors through `CreateErrorResult`.
- Register with the same helper the FTP package uses (`ServiceCollectionExtensions.cs` in
  `CrestApps.Core.AI.Mcp`, lines ~210–222 — `AddScoped<IMcpResourceTypeHandler>` plus the keyed
  registration). Publish a template by seeding one `McpResource` with `Source = Type` and the URI
  template — `ListTemplatesAsync` already surfaces it; `ListAsync` never lists figures.

### 15.3 Tools — two, not nine

| Tool | Arguments | Returns |
|---|---|---|
| `search_{instance}` (existing `DataSourceSearchToolFunction`) | `queries[]`, optional `contentTypes[]` | `DataSourceRetrievalResult` → text + `ResourceLinkBlock`s |
| `get_source` (new `KnowledgeObjectToolInstanceSource`, settings `{ DataSourceId }`) | `id` | the full object by canonical-id prefix: article → text; figure/chart → text + `ImageContentBlock`; table → text + rows as JSON |

`get_source` returns a `KnowledgeObjectToolResult : IMcpToolContentProvider` whose `ToString()`
is the text so it also works as a chat tool. Images larger than
`ChatDocumentsOptions.MaxVisionImageBytesPerFile` are returned as a link, not inline.

**Tests**: `DataSourceFigureResourceHandler_WrongDataSource_IsRefused`; `_Traversal_IsRejected`
(`SanitizePath`); `_HappyPath_ReturnsBlob`; `GetSource_FigureId_ReturnsTextAndImage`;
`GetSource_UnknownPrefix_ReturnsError`; `ListResources_NeverContainsFigures`.

---

# Part 3 — Indexers (where content comes from)

## 16. The connector abstraction

### 16.1 The pattern already exists

The web crawler subsystem is a general ingestion connector with `Web` in its names:

| Today | What it is | Generalized name |
|---|---|---|
| `IWebCrawlerStrategy` (`Name`, `ValidateAsync`, `DiscoverAsync → CrawledPageRef[]`, `FetchAsync → CrawledPage`) | discover + fetch, keyed by name | `IIngestionConnector` |
| `CrawledPageRef(Url, LastModifiedUtc, ChangeFrequency)` | item + change metadata | `IngestionItemRef(ItemId, ChangeToken, SizeBytes, LastModifiedUtc)` |
| `CrawledPage(Title, Content)` — **text only** | fetched item | `IngestionItemContent(Stream Content, string MediaType, string Title, string FileName)` |
| `WebCrawler` | a configured connector instance pointing at `AIDataSourceId` | `Indexer` (17) |
| `WebCrawlState` (Url, LastModifiedUtc, ChangeFrequency, ContentHash, LastIndexedUtc, LastSeenUtc) | per-item state | `IndexerItemState` |
| `WebCrawlerReindexPlanner` | diff discovery against state; enqueue new/changed; remove missing | `IndexerReindexPlanner` |
| `WebCrawlerReindexService` + `WebCrawlerReindexBackgroundService` (`PeriodicTimer`, `ReindexCheckIntervalMinutes`) | scheduling | `IndexerRunService` + `IndexerBackgroundService` |

The planner already refuses to tombstone when discovery returns nothing or throws (tests
`Planner_WhenDiscoveryReturnsNoPages_LeavesStateUntouchedAndReportsWarning`,
`Planner_WhenDiscoveryThrows_ReportsFailureAndEnqueuesNothing`). What is missing is the
**partial** case.

### 16.2 The contract

```csharp
public interface IIngestionConnector
{
    string Name { get; }
    ValueTask ValidateAsync(Indexer indexer, ValidationResultDetails result, CancellationToken ct = default);
    Task<IngestionDiscoveryResult> DiscoverAsync(Indexer indexer, CancellationToken ct = default);     // Items + IsComplete
    Task<IngestionItemContent> FetchAsync(Indexer indexer, string itemId, CancellationToken ct = default);   // null = gone/empty
}

public sealed record IngestionItemRef(string ItemId, string ChangeToken = null, long? SizeBytes = null, DateTimeOffset? LastModifiedUtc = null);
public sealed record IngestionDiscoveryResult(IReadOnlyList<IngestionItemRef> Items, bool IsComplete, string Message = null);
public sealed record IngestionItemContent(Stream Content, string MediaType, string Title = null, string FileName = null) : IAsyncDisposable;
```

`ChangeToken` is **opaque** — the planner compares it verbatim (`ETag` for web/blob, `mtime+size`
for SFTP/local, absent for FTP where the content hash is the only validator). The web strategy's
`LastModifiedUtc`/`ChangeFrequency` become the token string `"{lastModified:o}"` when present.

### 16.3 Reader resolution — media type first

`IIngestionDocumentReaderResolver.Resolve(fileName, mediaType, content)` (introduced in 4):

1. `mediaType` (declared by the connector or `IFormFile.ContentType`), when specific
   (not `application/octet-stream`): `GetKeyedService<IngestionDocumentReader>(mediaType)`.
2. **Magic bytes** on the first 8 bytes of a seekable stream: `%PDF-` → `application/pdf`;
   `PK\x03\x04` → zip container, disambiguate by extension (`.docx`/`.xlsx`/`.pptx`);
   PNG/JPEG via `MediaTypeHelper.HasValidImageSignature`. Rewind after sniffing.
3. Extension: `GetKeyedService<IngestionDocumentReader>(extension)` — today's behaviour.

`AddCoreAIIngestionDocumentReader<T>(params ExtractorExtension[])` additionally registers the
reader keyed by `MediaTypeHelper.InferMediaType(extension)` when that is specific. **Keyed
registrations resolve last-wins**, so the HTML conflict is resolved by order: `AddCoreAIDocumentProcessing`
registers the plain-text reader for `.html/.htm/text/html`, and `AddCoreIndexers` (17), which is
always called later, registers `HtmlIngestionDocumentReader` for the same keys.

**Tests** (`Core/Documents/Ingestion/IngestionDocumentReaderResolverTests.cs`):
`Resolve_DeclaredPdfMediaType_WinsOverTxtExtension`; `Resolve_OctetStream_FallsBackToSniffing`;
`Resolve_PdfMagicBytes_ReturnsPdfReaderAndRewindsStream`; `Resolve_ZipWithDocxExtension_ReturnsOpenXml`;
`Resolve_Unknown_FallsBackToExtension`; `Resolve_HtmlRegisteredTwice_LastWins`.

### 16.4 The assembled pipeline

```text
  WHERE                    WHAT                     MEANING                       SHAPE
IIngestionConnector  ->  IngestionDocumentReader ->  AIDocumentIngestionProcessor ->  KnowledgeObjectBuilder  ->  IKnowledgeObjectStore
  Web (adapter)            Pdf                       FigureCaption                    document/article               + IAIDataSourceIndexingQueue
  LocalFolder              OpenXml                   FigureSalience                   text / figure / chart / table
  AzureBlob                Html                      FigureDescription (inline off)
  Ftp / Sftp               PlainText                 Structure (phase 7)
                                     via IIngestionDocumentReaderResolver (16.3)   via IKnowledgeIngestionService (12.3)
```

Each layer is independently pluggable; a new connector is one class plus one registration; a new
file type is one reader plus one registration; neither touches retrieval.

### 16.5 Connectors

- **`WebIngestionConnector`** — adapter over `IWebCrawlerStrategy`: `Discover` maps
  `CrawledPageRef` → `IngestionItemRef`; `Fetch` returns the strategy's `CrawledPage.Content` as a
  `text/plain` stream with `Title`. Zero behaviour change for existing crawlers.
- **`LocalFolderIngestionConnector`** — `Directory.EnumerateFiles(root, pattern, recurse)`;
  item id = relative path; token = `"{mtimeTicks}:{length}"`; media type from extension;
  settings `{ RootPath, SearchPattern = "*.*", Recursive = true, MaxItems }`. Root must be under
  a host-allow-listed base path (`IndexerOptions.AllowedLocalRoots`); reject otherwise.
- **`AzureBlobIngestionConnector`** (`CrestApps.Core.Azure`) — `BlobContainerClient.GetBlobsAsync(prefix)`
  paged; token = `ETag`; `IsComplete = false` when a page fails; secrets through the same
  protector the Azure AI Search data source uses.
- **`FtpIngestionConnector`** / **`SftpIngestionConnector`** — lift `FtpConnectionMetadata` /
  `SftpConnectionMetadata` from the MCP packages into `CrestApps.Core.AI.Abstractions`; FTP has
  no reliable token → content hash only; SFTP token = `mtime:size`.

### 16.6 Risks specific to this layer

- **Partial discovery must never tombstone.** `LastSeenUtc` drives deletion. The planner performs
  removals **only when `IngestionDiscoveryResult.IsComplete` is true**, and records the fact on
  the indexer's `IndexerRunSummary` (17.3). Highest-severity item in Part 3.
- **Scale.** Per-indexer `MaxItemsPerRun`, per-indexer concurrency (default 2 fetches), resumable
  discovery by page token stored in `IndexerRunSummary.DiscoveryCursor`.
- **Cost.** First sync of 10,000 PDFs × 30 figures is 300,000 vision calls without controls. The
  per-indexer `FigureMode`, `MaxFigureDescriptionsPerDocument`, the content-hash cache and the
  backfill's `MaxConcurrentVisionCalls` are not optional.
- **Credentials.** Never store plain secrets in `Properties`; use `IDataProtectionProvider` as the
  FTP MCP handler does.

## 17. Indexers — the managed surface (D12, D13, D22)

An **indexer** is a configured job that discovers content somewhere, runs it through the
ingestion pipeline, and writes the resulting knowledge objects into one AI data source. "Web
crawler" becomes one kind of indexer.

### 17.1 This already exists — it is called `WebCrawler`

`src/Abstractions/CrestApps.Core.AI.Abstractions/Models/WebCrawler.cs` is, field for field, the
entity: `DisplayText`, `AIDataSourceId`, `Enabled`, `ReindexIntervalMinutes`, timestamps, owner,
plus `Source` (strategy key) and `Properties` (strategy settings). Around it: `IWebCrawlerStore`,
`EntityCoreWebCrawlerStore`, `YesSqlWebCrawlerStore` + `WebCrawlerIndex`, `WebCrawlerCatalogHandler`,
`CatalogRecordFactory` case, MVC area `WebCrawlers`, Blazor page `WebCrawlers`, view models.

**Rename map** (C# names only; persisted names unchanged — 0.4 rule 6):

| Today | Becomes |
|---|---|
| `WebCrawler` | `Indexer` (keep `[Obsolete] class WebCrawler : Indexer`-style alias for one release is *not* possible for a sealed class — instead keep the old type name as a `[Obsolete]` `using WebCrawler = Indexer;` global alias is also disallowed by repo rules; **so: rename outright and update every reference in the repository in the same PR**) |
| `IWebCrawlerStore` / `EntityCoreWebCrawlerStore` / `YesSqlWebCrawlerStore` / `WebCrawlerIndex` | `IIndexerStore` / `EntityCoreIndexerStore` / `YesSqlIndexerStore` / `IndexerIndex` — **table and collection names unchanged** |
| `WebCrawlState` / stores | `IndexerItemState` / stores (`Url` → `ItemId`, `ChangeFrequency` → `ChangeToken`; keep JSON property names via `[JsonPropertyName]` so stored records deserialize) |
| `WebCrawlerCatalogHandler` | `IndexerCatalogHandler` |
| `WebCrawlerReindexPlanner/Service/BackgroundService` | `IndexerReindexPlanner` / `IndexerRunService` / `IndexerBackgroundService` |
| `WebCrawlerOptions` | `IndexerOptions` (same members plus `AllowedLocalRoots`, `MaxConcurrentVisionCalls`) |
| `AddCoreWebCrawlers()` | `AddCoreIndexers()`; keep `AddCoreWebCrawlers()` as a thin `[Obsolete]` forwarder |
| Admin routes `/web-crawlers` | `/indexers`; keep a redirect from the old route |
| `Source = "Sitemap"` | unchanged |

### 17.2 Addition one — per-indexer model selection (D13)

```csharp
public string VisionDeploymentId { get; set; }      // null = Vision slot; text-only if nothing capable
public string UtilityDeploymentId { get; set; }     // null = Utility slot
public string EmbeddingDeploymentId { get; set; }   // null = the knowledge-base profile's embedding deployment
public FigureProcessingMode FigureMode { get; set; } = FigureProcessingMode.Auto;
public int? MaxFigureDescriptionsPerDocument { get; set; }
public int? MaxItemsPerRun { get; set; }
```

Rules:

- **Validated at save time** in `IndexerCatalogHandler.ValidatingAsync`: a chosen
  `VisionDeploymentId` must resolve (`IAIDeploymentManager.FindByIdAsync`) and satisfy
  `IAIDeploymentCapabilityService.SupportsFeatureOrUnconstrained(deployment, AIDeploymentFeatureNames.ImageInput)`,
  else `ValidationResult("The selected deployment does not accept image input.", [nameof(VisionDeploymentId)])`.
  Same for `EmbeddingDeploymentId` with `TextEmbedding`.
- **Null means "use the slot", never "disable".** `FigureMode = Off` is the way to disable vision.
- **A deleted or disabled deployment must not fail the run** — fall back to the slot, log once.
- Model choice belongs on the indexer, not the data source: one data source is fed by indexers
  with very different content.

The UI adds three deployment pickers (populated from `IAIDeploymentManager.GetAllBySlotAsync(slot)`),
a `FigureMode` select and two numeric fields to the existing crawler edit forms.

### 17.3 Addition two — run summary (D22)

Stored on the indexer record: `indexer.Put(new IndexerRunSummary { … })`.

```csharp
public sealed class IndexerRunSummary
{
    public DateTime StartedUtc { get; set; }  public DateTime? CompletedUtc { get; set; }
    public IndexerRunStatus Status { get; set; }           // Running | Succeeded | Failed | PartiallyCompleted
    public bool DiscoveryCompleted { get; set; }           // 16.6 — the deletion gate
    public string DiscoveryCursor { get; set; }
    public int ItemsDiscovered, ItemsIndexed, ItemsSkipped, ItemsDeleted, ItemsFailed;
    public int FiguresDescribed, VisionCallsMade;
    public string Error { get; set; }
}
```

Admin surface: list (name, connector, target, enabled, last run, status), create/edit/delete,
**Run now**, **Reset state** (delete `IndexerItemState` for the indexer → next run re-ingests
everything). The existing web crawler screens are the template.

### 17.4 What an indexer is not

Not a data source (it fills one). Not a reader (media type picks the reader). Not the chat-upload
path (files uploaded to a chat never involve an indexer).

### 17.5 Tests

Existing `WebCrawlerTests` (23) are renamed to `IndexerTests` with **unchanged assertions**. New:
`Handler_VisionDeploymentWithoutImageInput_IsRejected`; `Handler_NullDeployments_AreValid`;
`Planner_PartialDiscovery_PerformsNoDeletionsAndRecordsIncomplete`;
`Planner_CompleteDiscovery_DeletesMissing`; `RunService_DeletedVisionDeployment_FallsBackToSlotAndIndexes`;
`RunSummary_IsPersistedOnIndexer`; `LocalFolderConnector_RootOutsideAllowList_IsRejected`;
`LocalFolderConnector_ChangeTokenChangesWithMtimeOrLength`; `WebConnectorAdapter_MapsRefsAndContent`.

---

## 18. New and changed types, by project

**`CrestApps.Core.AI.Documents.Pdf`** — `PdfIngestionDocumentReader` (rewritten, 5), `PdfLayoutOptions`,
`PdfTextNormalizer`, `PdfPageLayout` (internal: words/blocks/order per page), `PdfImageExtractor` (internal, 5.3).

**`CrestApps.Core.AI.Documents`** — `Ingestion/`: `IAIDocumentIngestionPipeline` + `DefaultAIDocumentIngestionPipeline`,
`DocumentIngestionContext`, `AIDocumentIngestionProcessor`, `IIngestionDocumentReaderResolver` + default,
`IngestionDocumentElementExtensions.GetSemanticText`, `ElementMetadataKeys`, `FigureMetadataKeys`,
`FigureProcessingMode`, `FigureTier`; `Ingestion/Processors/`: `FigureCaptionProcessor` (+ `CaptionPatternOptions`,
`IFigureCaptionCandidateDetector`, `IFigureCaptionResolver`), `FigureSalienceProcessor` (+ `FigureSalienceOptions`, `PngSampler`),
`FigureDescriptionProcessor` (+ `IFigureDescriptionCache`, `MemoryFigureDescriptionCache`);
`Knowledge/`: `KnowledgeObjectBuilder`, `KnowledgeObjectTypes`, `KnowledgeObjectStatus`, `FigureDetails`, `ChartDetails`,
`TableDetails`, `ArticleDetails`, `IKnowledgeIngestionService` + default, `FigureDescriptionBackfillService`,
`IngestedAIDataSourceSourceHandler`, `IngestedReferenceLinkResolver`, `KnowledgeObjectCatalogHandler`, figure download endpoint;
`Services/`: `IImageAnalysisService.AnalyzeAsync(ImageAnalysisRequest)`, `ImageAnalysisRequest`; `DefaultAIDocumentProcessingService`
(pipeline + `GetSemanticText` + figure block + `DocumentFigureList`).

**`CrestApps.Core.AI`** — `Templates/Prompts/figure-transcription.md`, `AITemplateIds.FigureTranscription`;
`DataSourceSearchIndexProfileHandler.BuildFields` (+4 columns); `DefaultAIDataSourceIndexingService` (pre-chunked rows,
`TryAddFieldsAsync`, `DeleteByReferenceIdsAsync`); `DataSourceRetrieval.SearchDetailedAsync`, `DataSourceRetrievalResult`,
`RetrievedFigure`, `RetrievedTable`; `DataSourceSearchToolSettings.ContentTypes`; `Tooling/Instances/Knowledge/`:
`KnowledgeObjectToolInstanceSource`, `GetSourceToolFunction`, `KnowledgeObjectToolResult`.

**`CrestApps.Core.AI.Abstractions`** — `AIDataSourceSourceTypes.Ingested`, `KnowledgeObject`, `IKnowledgeObjectStore`,
`Indexer` (renamed `WebCrawler` + 17.2 members), `IndexerItemState`, `IndexerRunSummary`, `IndexerRunStatus`,
`IIndexerStore`, `IIndexerItemStateStore`, `IIngestionConnector`, `IngestionItemRef`, `IngestionDiscoveryResult`,
`IngestionItemContent`, `FtpConnectionMetadata` / `SftpConnectionMetadata` (lifted).

**`CrestApps.Core.Infrastructure.Abstractions`** — `SourceDocument.IsPreChunked`; `DataSourceSearchResult` (+`ContentType`,
`RootId`, `ParentId`, `Page`, `Filters`); `ISearchIndexManager.TryAddFieldsAsync` (default); `IDataSourceContentManager.DeleteByReferenceIdsAsync` (default).

**`CrestApps.Core.Infrastructure`** — `DataSourceConstants.ColumnNames.{ContentType, RootId, ParentId, Page}`.

**Providers** (`CrestApps.Core.PostgreSQL`, `CrestApps.Core.Elasticsearch`, `CrestApps.Core.Azure.AISearch`) — `TryAddFieldsAsync`,
`DeleteByReferenceIdsAsync`, four extra columns in `SearchAsync` `Select` and mapping.

**`CrestApps.Core.AI.WebCrawlers` → `CrestApps.Core.AI.Indexers`** (project rename is optional; namespace rename is
in scope) — the 17.1 rename map, `WebIngestionConnector`, `LocalFolderIngestionConnector`, `IIngestionConnectorResolver`,
`IngestionConnectorOptions`, `HtmlIngestionDocumentReader` registration for `.html/.htm/text/html`.

**`CrestApps.Core.Azure`** — `AzureBlobIngestionConnector`. **New `CrestApps.Core.AI.Indexers.Ftp` / `.Sftp`** or the
existing MCP Ftp/Sftp packages — `FtpIngestionConnector`, `SftpIngestionConnector`.

**`CrestApps.Core.AI.Mcp`** — `McpToolResultMapper`, `IMcpToolContentProvider`, `DataSourceFigureResourceConstants`,
`DataSourceFigureResourceHandler`.

**Stores** (`CrestApps.Core.Data.EntityCore`, `CrestApps.Core.Data.YesSql`) — `KnowledgeObject` store pair + index + schema +
factory case; renamed indexer/item-state stores; DI registration methods (Appendix B).

**Sample hosts** (`CrestApps.Core.Mvc.Web`, `CrestApps.Core.Blazor.Web`, `CrestApps.Core.Startup.Shared`) — Indexers area/pages
(renamed), deployment pickers, Run now / Reset state, data-source "Upload files" action, YesSql schema creation calls.

**Docs** — `src/CrestApps.Core.Docs/docs/core/document-processing.md` (pipeline, processors, options),
`core/ai-documents.md` (figures in chat uploads), `data-sources/ingested.md` (new), `data-sources/web-crawlers.md` →
`data-sources/indexers.md` (rename, keep old file as a redirect stub), `mcp/server.md` (result mapper, `get_source`),
`mcp/resource-types.md` (`datasource-figure`), `core/tool-instances.md`, and a bullet per phase in `changelog/2.0.0.md`.

---

## 19. Phased build order — the executable checklist

Each phase is one PR, independently shippable, and leaves every earlier test green. **Done when**
is the merge criterion. "Files" lists what the PR creates (**C**) or modifies (**M**). Section
references point to the design the phase implements; the phase text does not repeat it.

### Phase 0 — Pin today's behaviour (no production code changes except test seams)

- **Goal:** the regression contract exists before anything moves.
- **Files:** C `tests/…/Support/PdfFixtureBuilder.cs`, C `tests/…/Support/TestImageFactory.cs`,
  C `tests/…/Core/Documents/Regression/ChatDocumentRegressionTests.cs`, C `tests/…/Core/Indexing/DefaultAIDataSourceIndexingServiceTests.cs`
  (the Moq harness of 13.6 with the two "today" tests), C `tests/…/Core/Services/DataSourceRetrievalTests.cs`
  (`SearchAsync_TextUnchangedForTextOnlyResults` capturing today's rendering verbatim).
- **Steps:** build the fixture builder (Appendix A.1 writer API); write the 21.5 pinned tests
  against current code; make them pass without touching `src/`.
- **Done when:** both commands green; the new tests exist and pass on `d733a534` behaviour.

### Phase 1 — Pipeline seam, reader rewrite, semantic flattening

- **Design:** 4, 5.1–5.2, 5.4–5.6, 9.1, 9.2 (text parts only), 10.
- **Files:** C `Ingestion/*` (4, 10), C `IngestionDocumentElementExtensions.cs`, M `PdfIngestionDocumentReader.cs`,
  C `PdfLayoutOptions.cs`, C `PdfTextNormalizer.cs`, M `PdfServiceCollectionExtensions.cs`, M `ServiceCollectionExtensions.cs`
  (Documents: pipeline + resolver + processor registration API), M `DefaultAIDocumentProcessingService.cs`
  (two call sites only), M docs `document-processing.md`, M `changelog/2.0.0.md`.
- **Steps:** (1) add the pipeline, context, processor base, resolver (extension-only) and
  registration — no processors yet; (2) switch `DefaultAIDocumentProcessingService` to the pipeline
  and `GetSemanticText`; run phase 0 tests — must still pass; (3) rewrite the reader behind
  `UseLayoutAnalysis` with the legacy path kept; (4) decoration guard; (5) text normalization;
  (6) tests of 4 and 5.6 (text ones).
- **Done when:** all 5.6 text tests pass; `ReadAsync_LayoutAnalysisDisabled_MatchesLegacyOutput`
  passes; phase 0 pins pass; `.xlsx/.csv` tests untouched and green.

### Phase 2 — Images, captions, salience (no vision)

- **Design:** 5.3, 6, 7, 9.2 (figure block with caption only), 9.3.
- **Files:** M reader (image emission), C `Processors/FigureCaptionProcessor.cs` + options + interfaces,
  C `Processors/FigureSalienceProcessor.cs` + options + `PngSampler.cs`, M Documents `ServiceCollectionExtensions.cs`
  (register both processors in order), M `DefaultAIDocumentProcessingService.cs` (figure block, bytes to file store,
  `DocumentFigureList`), M docs, M changelog.
- **Steps:** image emission → caption processor → salience processor → flattening block → tests.
- **Done when:** 5.6 image tests, 6.7, 7.4, 9.3 (caption-only cases) pass; the phase 0 pin
  `PlainTextPdf_ProducesSameWordsAsBefore` still passes (images add no words without vision).

### Phase 3 — Vision descriptions (solves the original problem for chat uploads)

- **Design:** 8, 9.2 (described figures), `ImageAnalysisRequest`.
- **Files:** M `IImageAnalysisService.cs`, M `DefaultImageAnalysisService.cs`, C `ImageAnalysisRequest.cs`,
  C `Templates/Prompts/figure-transcription.md`, M `AITemplateIds.cs`, C `Processors/FigureDescriptionProcessor.cs`,
  C `IFigureDescriptionCache.cs` + memory impl, M registration, M docs, M changelog.
- **Done when:** 8.6 and remaining 9.3 tests pass; manual acceptance (20.3) answers the R²
  question from a chat upload of a synthetic chart fixture whose description is stubbed.

### Phase 3A — Provider-backed structure reader (proposed, not yet approved)

- **Design:** 24. **Slots after phase 3 but depends on nothing after it**; it can equally be run beside
  phase 9, which is where the resolver is completed. Running it *before* phases 5–8 improves their input.
- **Goal:** where an operator has a document-understanding service, stop inferring structure and read it.
  PdfPig and the heuristics stay as the offline fallback, unchanged.
- **Files:** C project `src/Primitives/CrestApps.Core.AI.Documents.DocumentIntelligence/` (own package, so
  the core takes no Azure dependency), C `DocumentIntelligenceIngestionDocumentReader.cs`,
  C `DocumentIntelligenceOptions.cs`, C `FallbackIngestionDocumentReader.cs`, C `ServiceCollectionExtensions.cs`,
  M `FigureCaptionProcessor.cs` + `FigureSalienceProcessor.cs` (honour provider-supplied metadata),
  M `FigureMetadataKeys.cs` (`CaptionSources.Provider`), M docs, M changelog.
- **Steps:** (1) the options and the opt-in registration; (2) the reader and its mapping (24.2);
  (3) figure bytes (24.3); (4) the fallback decorator (24.4); (5) make the processors no-op on
  provider-supplied metadata (24.5); (6) tests.
- **Tests:** mapping from a hand-written synthetic `AnalyzeResult` to elements — paragraph roles, reading
  order, tables with spans, figures with captions; `Reader_ServiceThrows_FallsBackToPdfPig`;
  `Reader_NotConfigured_IsNotRegistered`; `CaptionProcessor_ProviderCaption_IsNotOverwritten`;
  `Salience_ProviderCaption_ScoresAsPattern`. **No captured customer document is ever committed** — the
  `AnalyzeResult` fixtures are written by hand.
- **Done when:** the same synthetic PDF fixtures used in phases 1–3 produce equivalent or better elements
  through the provider reader; every phase 0 pin still passes; and with the provider unconfigured the
  pipeline behaves exactly as it does today.

### Phase 4 — Typed knowledge: store, `Ingested` source type, schema, manual upload

- **Design:** 11, 12.1–12.3, 12.5, 13 (all), Appendix B for the store.
- **Files:** C `KnowledgeObject.cs` + details classes + `IKnowledgeObjectStore.cs`, C EntityCore/YesSql stores + index +
  schema builder, M `CatalogRecordFactory.cs`, C `KnowledgeObjectCatalogHandler.cs`, C `KnowledgeObjectBuilder.cs`,
  C `IKnowledgeIngestionService` + default, C `IngestedAIDataSourceSourceHandler.cs`, C `IngestedReferenceLinkResolver.cs`,
  C figure download endpoint, M `AIDataSourceSourceTypes.cs`, M `SourceDocument.cs`, M `DataSourceConstants.cs`,
  M `DataSourceSearchIndexProfileHandler.cs`, M `ISearchIndexManager.cs`, M `IDataSourceContentManager.cs`,
  M `DataSourceSearchResult.cs`, M `DefaultAIDataSourceIndexingService.cs`, M three provider content managers + index managers,
  M MVC/Blazor data-source pages (Upload files), M YesSql schema creation in `YesSqlServiceCollectionExtensions.cs`,
  C docs `data-sources/ingested.md`, M changelog.
- **Steps (in this order):** schema constants + `BuildFields` + `TryAddFieldsAsync` → `IsPreChunked` +
  `DeleteByReferenceIdsAsync` in the indexing service (13.6 tests) → entity + store (Appendix B) →
  builder (11.5 tests) → ingestion service + handler (12.6 tests) → providers' result mapping (13.5)
  → upload action.
- **Done when:** 11.5, 12.6 (non-backfill), 13.6 tests pass; an `Ingested` data source accepts a
  synthetic PDF through the upload action and `search_` returns typed rows; every earlier pin green.

### Phase 5 — Backfill and typed retrieval

- **Design:** 12.4, 14.
- **Files:** C `FigureDescriptionBackfillService.cs`, M `DataSourceRetrieval.cs`, C `DataSourceRetrievalResult.cs` + records,
  M `DataSourceSearchToolSettings.cs`, M `DataSourceSearchToolFunction.cs` (`contentTypes` argument), M docs `tool-instances.md`, M changelog.
- **Done when:** 12.6 backfill tests, 14 tests pass; `SearchAsync_TextUnchangedForTextOnlyResults` (phase 0) still passes.

### Phase 6 — MCP

- **Design:** 15.
- **Files:** C `McpToolResultMapper.cs`, C `IMcpToolContentProvider.cs`, M `McpServerBuilderExtensions.cs` (three sites),
  C `DataSourceFigureResourceConstants.cs`, C `DataSourceFigureResourceHandler.cs`, M Mcp `ServiceCollectionExtensions.cs`,
  C `Tooling/Instances/Knowledge/*` (`get_source`), M docs `mcp/server.md`, `mcp/resource-types.md`, M changelog.
- **Done when:** 15 tests pass; existing `McpServerBuilderExtensionsTests` green; `resources/list`
  test proves no figures are listed.

### Phase 7 — Structure analysis (articles, folios, section labels)

- **Design:** 11.1 (real articles), D11. Implement `IDocumentStructureAnalyzer` with
  `TocSeededStructureAnalyzer`: (1) TOC page = the page with ≥ 5 lines matching `.+\s+\d{1,3}$`
  in the first 4 pages; (2) seeds = (title, author?, printed page) per line; (3) folio capture =
  a 1–3 digit token in a decoration block near the top or bottom edge, mapped index→folio;
  (4) boundaries = pages where a heading (block with `ModalPointSize` ≥ 1.5 × body) fuzzy-matches
  a seed title (`Distances.MinimumEditDistanceNormalised` ≤ 0.3); (5) section label = decoration
  block text that recurs on ≥ 2 pages and matches `^[A-ZÁÉÍÓÖŐÚÜŰ ]{6,}$`; pages with no label
  and no matched heading become `article type = advertisement` and are **excluded from
  embedding** but kept as objects (`Status = Excluded`). Publication metadata = one utility-model
  extraction over page 1–2 decoration + masthead text with template `publication-metadata.md`,
  result into `ArticleDetails`/document `Properties`. **Degrade:** any step failing → one article.
- **Files:** C `Knowledge/Structure/*`, M `KnowledgeObjectBuilder.cs`, C template, tests
  `Core/Documents/Knowledge/TocSeededStructureAnalyzerTests.cs` (synthetic TOC fixture; no TOC
  → one article; folio offset ≠ index; advert page excluded).
- **Done when:** the analyzer's tests pass and a document without front matter yields exactly one
  article identical to phase 4's output.

### Phase 8 — Tables, vector figures, exact chart series

- **Design:** 11.4 (`Exact`/`AxesOnly`), `IngestionDocumentTable` emission. Tables: ruled tables
  from `page.ExperimentalAccess.Paths` horizontal/vertical segments forming ≥ 2×2 cells; grid
  tables from words whose x-starts cluster into ≥ 3 columns across ≥ 3 lines. Emit
  `IngestionDocumentTable(markdown, cells) { Text = markdown, PageNumber = n }`. Vector figures:
  clusters of ≥ 20 path segments inside a region with < 5 % text coverage → an
  `IngestionDocumentImage` rendered as a PNG line drawing by `PngSampler`'s sibling `PngWriter`
  (test helper promoted to production, ~120 lines) with `ValueConfidence = Exact` when tick labels
  and series paths are both found.
- **Files:** C `PdfTableDetector.cs`, C `PdfVectorFigureDetector.cs`, C `IChartDataExtractor` + `VectorPathChartDataExtractor`,
  M reader, M builder, tests with `DrawLine`/`DrawRectangle` fixtures.
- **Done when:** a drawn 3×3 ruled fixture yields a table with 9 cells; a drawn polyline with
  tick labels yields `Exact` points within 1 % of the drawn values; a raster chart stays `Descriptive`.

### Phase 9 — Connector abstraction, media-type-first readers, `LocalFolder`

- **Design:** 16.2–16.5 (Web adapter + LocalFolder only), 4's resolver completed.
- **Files:** C connector contracts (Abstractions), C `WebIngestionConnector.cs`, C `LocalFolderIngestionConnector.cs`,
  M `DefaultIngestionDocumentReaderResolver.cs` (media type + sniffing), M `AddCoreAIIngestionDocumentReader` (media-type keys),
  M crawler planner/service to consume `IIngestionConnector` through the adapter, M docs, M changelog.
- **Done when:** 16.3 tests pass; the 23 crawler tests pass unchanged; a `LocalFolder` indexer
  pointed at a temp folder of synthetic PDFs populates an `Ingested` data source end-to-end (one
  integration-style test with the real pipeline and Moq'd index writer).

### Phase 10 — Indexers surface

- **Design:** 17 (rename, model selection, run summary, admin actions).
- **Files:** the 17.1 rename across Abstractions, Indexers project, both stores, both hosts, tests;
  M `Indexer.cs` (+17.2), C `IndexerRunSummary.cs`, M handler validation, M planner (completeness
  gate), M UI, M docs (`indexers.md`), M changelog.
- **Done when:** 17.5 tests pass; renamed crawler tests pass; saving a non-vision deployment as
  `VisionDeploymentId` is rejected in the UI and in the handler test.

### Phase 11 — Remote connectors and hardening

- **Design:** 16.5 (Blob, FTP, SFTP), 16.6.
- **Done when:** each connector has discover/fetch tests against an in-memory fake of its client;
  the partial-discovery test proves zero deletions; a 10,000-item fake container respects
  `MaxItemsPerRun` and resumes from `DiscoveryCursor`.

**Ordering notes.** Phases 1–3 solve the original problem for chat uploads and stand alone.
Phases 4–6 make the data source typed and exposed; 4 is the largest and must not be split.
Phase 7 and 8 are quality; 9–11 are intake. **Phase 9 depends only on phase 4**, and **phase 10
depends only on 3 and 9** — if continuous intake matters more than citation precision, run 9–10
before 7–8.

---

## 20. Verification strategy

### 20.1 Fixtures — synthetic, generated in-test

`tests/CrestApps.Core.Tests/Support/PdfFixtureBuilder.cs` wraps `UglyToad.PdfPig.Writer.PdfDocumentBuilder`:

```csharp
var pdf = new PdfFixtureBuilder()                       // A4: 595 x 842 points, origin bottom-left
    .Page(p => p.Text("LEKTORÁLT CIKK", x: 40, y: 810, size: 8)          // running head
                .Text("Body paragraph one.", 40, 760, 11)
                .Png(TestImageFactory.BarChart(200, 120), x: 40, y: 500, w: 200, h: 120)
                .Text("1. ábra. A chart caption.", 40, 485, 9))
    .Page(p => p.Text("LEKTORÁLT CIKK", 40, 810, 8).Text("Body two.", 40, 760, 11))
    .Build();                                            // MemoryStream
```

Two-column fixtures place left-column text at x = 40 and right-column at x = 310 with
**interleaved `AddText` calls** (right, left, right, left) so the raw content stream is out of
order and the reading-order test is meaningful. Fonts: `builder.AddStandard14Font(Standard14Font.Helvetica)`
and `HelveticaBold` for headings (larger `size` drives `PointSize`).

`TestImageFactory.CreatePng(width, height, Func<int,int,(byte r, byte g, byte b)> pixel)` — a
minimal PNG encoder (`ZLibStream`, CRC32 table); helpers `BarChart(w,h)` (6 flat colours, axes as
1-px dark lines), `Noise(w,h,seed)` (photo proxy), `Solid(w,h,rgb)`. **No binary fixtures are
committed; no third-party document is ever used.**

### 20.2 Test inventory by phase

The tests named in sections 4–17 are the inventory; each phase's **Done when** lists which must
pass. Two categories deserve calling out:

- **Pinned regression tests (21.5)** run in every phase and are never edited to pass — if one
  fails, the change is wrong.
- **Provider string builders** (SQL for PostgreSQL, OData for Azure, query JSON for Elasticsearch)
  are unit-tested as pure functions; the repository has no live-provider tests and this plan adds
  none.

### 20.3 Manual acceptance (not automated)

On a developer machine with a vision deployment configured, against the reference document
(never committed):

> "What is the correlation between the ÉKM and TNM specific heating primary-energy figures?"

must retrieve p4 and answer with **R² = 0,8858**, cite the figure, and — over MCP — return a
`ResourceLinkBlock` whose URI resolves to the correct JPEG. Expected salience on that document:
~60 skip, ~10 caption-only, ~30 describe.

---

## 21. Regression audit — what today's behaviour depends on

This is the audit, not a reassurance. Each row was checked against the tree at `d733a534`.

### 21.1 Chat document upload (`DefaultAIDocumentProcessingService`)

Path: `IFormFile` → keyed reader → `EnumerateContent().Select(e => e.Text)` → `NormalizeDocumentChunksAsync`
(or `ChunkRawContent` when tabular) → `AIDocument` + chunks.

| Change | Effect on today's behaviour | Verdict |
|---|---|---|
| Reader emits many paragraphs per page | join on `\n` then re-chunk at 500 tokens: newline positions move, words do not | **Safe**, and better ordered |
| Reader emits `IngestionDocumentImage` | `Text` is `null` (measured) and the existing `Where(!IsNullOrWhiteSpace)` drops it; `GetSemanticText` returns the caption only once a caption processor has run | **Safe by construction** until phase 2; from phase 2 captions are *added* text |
| `GetSemanticText` replaces `.Text` | identical for paragraphs; only images/tables differ and no reader emits those today | **Safe for every existing file type** |
| Decoration stripping | the one genuine loss of text; guarded by 5.2 and switchable | **Intentional, guarded, tested** |
| Ligature normalization | changes stored text for the better; nothing round-trips chunk text to a file | **Safe** |
| Tabular (`.xlsx/.csv`) | `IsTabularFileExtension` routes to `ChunkRawContent` before any of this; readers unchanged | **Unaffected** |
| Pipeline service in the middle | same reader resolution (extension) in phase 1; processors list is empty until phase 2 | **Safe** |

### 21.2 Existing data sources

| Source type | Touched? |
|---|---|
| `SearchIndexProfile`, `Elasticsearch`, `AzureAISearch`, `PostgreSQL` | Only by 13.1's `contentType = "text"` on new rows and the extra `Select` columns — additive. |
| `Web` | Handler untouched (D20). Phase 9 wraps `IWebCrawlerStrategy` in an adapter; phase 10 renames types. The 23 crawler tests pin behaviour. |

### 21.3 The index schema — verified, and one pre-existing suspect

- `filters` is **not** in `DataSourceSearchIndexProfileHandler.BuildFields`. PostgreSQL adds a JSONB
  column lazily; Elasticsearch maps dynamically; **Azure AI Search creates only declared fields and
  `AzureAISearchDocumentManager` writes every `IndexDocument.Fields` key verbatim.** Azure rejects
  documents with undeclared properties, so either `filters` is silently failing for `Web` sources
  on Azure today, or something not found in this audit declares it. **Action (phase 4, step 1):**
  write a test that builds the Azure `SearchDocument` for a row with `filters` and confirm against
  a real index once; record the outcome in 22.11. This is why D16 puts the typed discriminators in
  **declared columns** rather than in `filters`.
- Adding nullable columns is additive on all three providers; rows written before the change read
  `contentType` as `text`. `TryAddFieldsAsync` (13.2) is the only new schema operation and is
  default-implemented so third-party providers keep compiling.

### 21.4 Defects the typed model would hit, now fixed by design

- **1000 chunk ids per reference** (`BuildChunkIds`) → 40,000 delete ids for 40 typed rows.
  D21 / 13.4.
- **No delete by predicate anywhere** (`ISearchDocumentManager.DeleteAsync` is id-list only;
  `IDataSourceContentManager` deletes by data source only). 13.4 adds it per provider using each
  provider's existing `DeleteByDataSourceIdAsync` technique.
- **No way to add a field to an existing index.** 13.2.
- **`DataSourceSearchResult` cannot carry a type.** 13.5.

### 21.5 Pinned tests — written in phase 0, never edited

- `ChatDocumentRegressionTests.PlainTextPdf_WordSetIsSupersetOfLegacyOutput` — the set of words
  from the new reader ⊇ legacy words minus decoration; and equal when `StripDecoration = false`.
- `ChatDocumentRegressionTests.PdfWithImages_ProducesTextOnlyBeforePhase3` — no `[figure` marker
  when no vision deployment is configured.
- `ChatDocumentRegressionTests.Xlsx_And_Csv_OutputIsByteIdentical` — against a captured expected
  string for a small generated workbook/CSV.
- `DefaultAIDataSourceIndexingServiceTests.PlainSource_RowFieldsMatchToday` — the exact field set
  and `chunkId` shape for a non-pre-chunked source (plus `contentType = text` from phase 4 on —
  this is the one sanctioned edit, made in phase 4 and noted in the test).
- `DataSourceRetrievalTests.SearchAsync_TextUnchangedForTextOnlyResults`.
- `WebCrawlerTests.*` (23) — unchanged assertions through the rename.
- `McpServerBuilderExtensionsTests.CallTool_StringResult_IsSingleTextBlock`.

---

## 22. Open questions

1. ~~Should `DataSourceSearchResult` grow?~~ **Closed — yes** (13.5).
2. ~~Article vs page?~~ **Closed — article; degrade to one** (D6, D11).
3. ~~Ad exclusion by text density?~~ **Closed — running-head label** (phase 7).
4. **Document profiles.** Periodical / manual / report / datasheet as an explicit
   `DocumentProfile` guiding structure inference — build in phase 7 or defer? *Position: defer;
   D11's degrade path covers non-periodicals.*
5. **Does `get_source` stay one tool?** Revisit if id kinds exceed six.
6. **Chart estimate tier.** *Position: none. Revisit only with evidence that flagged estimates
   are not laundered by summarizing models.*
7. **Embedding model for accented languages.** Confirm the deployment is multilingual and
   `RagTextNormalizer` keeps accents. Add a fixture test in phase 4.
8. **Figure access control.** Canonical ids are guessable. 15.2 checks data-source membership;
   the download endpoint (12.2) must apply the same authorization as document downloads. *Assumed;
   flagged so phase 6 does not forget.*
9. **One indexer → several data sources?** *Position: no.*
10. **Run history store vs `IndexerRunSummary` on the record** (D22). Revisit when operators ask
    for history.
11. **Azure `filters` round-trip** (21.3). Resolve in phase 4 step 1 and record the answer here.
12. **Cron schedules for indexers.** `ReindexIntervalMinutes` is an interval; nightly-at-02:00
    wants cron. Defer until asked.
13. **Should `Web` data sources be migratable to `Ingested`** so crawled pages get figures and
    typed rows? An opt-in "convert" action after phase 10.
14. **`UnsupervisedReadingOrderDetector.Instance` also uses rendering order** (`useRenderingOrder`
    defaults on), so it returns the right column before the left on the interleaved fixture of 5.6 —
    the very case 20.1 builds the fixture to prove. Phase 1 therefore constructs the detector
    explicitly as `new UnsupervisedReadingOrderDetector(5, SpatialReasoningRules.ColumnWise,
    useRenderingOrder: false)` instead of using `Instance`. *Rationale: content-stream order is exactly
    what cannot be trusted on a multi-column page; correct Appendix A.1 to name the constructor.*
15. **The Standard-14 writer does not round-trip `U+00B2`.** `PdfDocumentBuilder` + Helvetica writes it
    and PdfPig reads it back as a different glyph, so the `R² = 0,8858` half of
    `ReadAsync_LigatureWord_IsNormalized` (5.6) cannot be asserted from a generated fixture. Phase 1
    asserts the ligature expansion and the decimal-comma preservation in the reader test, and asserts
    superscript preservation in `PdfTextNormalizerTests` instead, which feeds the string straight in.
    *Rationale: a fixture-writer limitation, not a reader behaviour; the coverage is preserved.*
16. **Caption direction is learned only from a one-to-one pairing.** 6.4 says a sample counts when a
    pattern-matched caption has exactly one image within 3 line heights. Taken literally, the ambiguous
    page 6.7's `Process_LearnedDirection_*` tests build — one figure between a caption above and a caption
    below — contributes two samples and votes on the very question the prior exists to settle, which makes
    the four-sample test converge at five. Phase 2 additionally requires that image to have exactly one
    pattern-matched caption near it. *Rationale: "confident" has to mean unambiguous in both directions.*
17. ~~The salience weights let a captioned, flat-coloured repeat survive as `CaptionOnly`.~~ **Closed by
    19 — the pixel signals are gone.**
19. **The pixel salience signals are removed (7.1, 7.3), and `PngSampler` with them.** Measured over a real
    23-page trade magazine: **82 of 103 image XObjects are `DCTDecode`**, so a PNG-only sampler decoded
    **20 of 100** emitted figures and the colour/run signals decided almost nothing. The repeat signal fared
    worse — **0 hashes appeared on 3+ pages**, because advertisers use different artwork on every page — so
    nothing pushed ads or logos negative and the tiers came out **Skip 14 / CaptionOnly 61 / Describe 25**
    against 20.3's expected ~60 / ~10 / ~30.
    Replacing them with caption evidence weighted by source (`PatternCaptionScore` 3,
    `TypographyCaptionScore` 2, `CitedInProseScore` 2) and raising `CaptionOnlyThreshold` from 0 to 1 gives
    **Skip 57 / CaptionOnly 19 / Describe 24** on the same file. *Rationale: a printed numbered caption is
    much stronger evidence than "this block was in smaller type"; the pixels were never the discriminator.*
20. **`PdfTextNormalizer` assumed a Latin script (5.4).** On the same file **747 words stayed split by a
    line-break hyphen**, which 5.4 never considered and which hurts German, Hungarian, Finnish and Dutch far
    more than English. Phase 1's normalizer now de-hyphenates, composes accents with `FormC` (safe for `R²`,
    unlike `FormKC`), expands Armenian and Arabic presentation forms as well as Latin ligatures, and strips
    invisible formatting characters while keeping ZWNJ/ZWJ. Broken words fell **747 to 31**; the remainder
    are hyphens spanning two blocks, which is out of scope. A new `TextSegmentation` helper replaces the
    `.!?`-and-capital-letter rules in the decoration guard and the reference scanner, which never fired in
    caseless scripts. *Rationale: none of this is language-specific work; it was a Latin-script assumption.*
21. **`SentenceBoundaryDetector` in the realtime path has the same Latin-only limitation.** It is a
    streaming detector for speech chunking with English abbreviation rules, so it was not reused here, but it
    could adopt `TextSegmentation`'s terminator set. *Position: out of scope for these phases; flagged so a
    realtime session in Japanese or Arabic is not assumed to chunk correctly.*
22. **Should a provider-backed reader supersede the heuristics?** Azure AI Document Intelligence
    `prebuilt-layout` returns paragraphs with roles, reading order, tables with spans and **figures with
    captions** — sections 5–8 and part of 11, in any language. The D18 seam
    (`IIngestionDocumentReaderResolver`) already allows a second reader to win, with the caption and salience
    processors no-oping when the reader supplied that metadata, and PdfPig staying the offline fallback.
    *Position: add it as its own phase; see the write-up requested alongside this entry.*
23. **Phase 8's table detection should use `tabula-sharp` (MIT, built on PdfPig)** rather than a hand-written
    `PdfTableDetector`. *Position: adopt when phase 8 starts.*
18. **Phase 0's Files list omits `McpServerBuilderExtensionsTests`,** but 21.5 names
    `CallTool_StringResult_IsSingleTextBlock` among the pinned tests written in phase 0. Phase 0 added
    it to the existing file. *Rationale: 21.5 is explicit that the pinned set is written in phase 0.*

## 23. Risks

| Risk | Mitigation |
|---|---|
| Vision cost on a large corpus | Three tiers + per-document budget + content-hash cache (store-backed from phase 4) + `FigureMode.Off` + backfill concurrency cap |
| Vision latency blocking ingest | D7 — inline only in chat (capped); indexer path backfills and re-queues single ids |
| Layout heuristics wrong on an unseen corpus | `UseLayoutAnalysis = false` reproduces today exactly; `StripDecoration` off; pluggable detector/resolver |
| **Decoration stripping removes body text** | 5.2 guard, tested; pinned superset test |
| Description quality drifts with model/template changes | prompt version in the cache key; fixture assertions on transcription content |
| **Fabricated chart values presented as data** | D9 / 11.4 — `Exact` only from geometry or printed labels; `ValueConfidence` stored and rendered |
| Structure inference wrong on a document with no TOC | D11 degrade — one article, hierarchy still valid |
| Phase 4 schema change breaks existing knowledge bases | additive nullable columns; `TryAddFieldsAsync` default-implemented; null reads as `text`; no forced reindex |
| **`BuildChunkIds` amplification** | D21 — provider delete by reference ids with fallback |
| **Partial discovery tombstones a knowledge base** | `IsComplete` gate on the planner; recorded in `IndexerRunSummary` |
| Rename breaks existing crawler records | persisted names unchanged; `[JsonPropertyName]` on renamed members; 23 pinned tests |
| Operator picks a non-vision deployment | D13 save-time validation; run-time slot fallback, never a failed run |
| Deployment deleted after selection | fall back to the slot, log once, keep indexing |
| Scope creep — typed model never ships | phases 1–3 deliver the original capability standalone; 4 is one PR by design |
| `KnowledgeObject` store becomes a second source of truth vs the index | the store is authoritative for *objects*; the index is a projection rebuilt by `SyncDataSourceAsync`; `Reset state` + full sync recreates it |

---

## 24. Provider-backed structure reader (design for phase 3A)

### 24.1 Why

Sections 5–8 infer, from geometry, things a document-understanding service already reports: which block is a
heading, which is a running head, what the reading order is, where the tables are and what their cells span,
where the figures are and what captions belong to them. Those inferences are what sections 22.14–22.20 kept
having to correct, and each correction is language- and layout-specific work we do not want to own.

**Azure AI Document Intelligence** `prebuilt-layout` returns all of it, for any language, and has an on-prem
container for hosts that cannot call the cloud. AWS Textract and Google Document AI are equivalent and would
slot behind the same abstraction.

This does not replace anything. The D18 seam already lets a second reader win, so the heuristics become the
offline fallback rather than the only path — which is also the answer for a host with no key, no budget, or
no network.

### 24.2 Mapping

One call per document, then `AnalyzeResult` maps onto the element model we already have:

| Provider output | Element |
|---|---|
| `Paragraph` with role `pageHeader` / `pageFooter` / `pageNumber` | `IngestionDocumentHeader` / `IngestionDocumentFooter`, `IsDecoration = true` |
| `Paragraph` with role `title` / `sectionHeading` | `IngestionDocumentParagraph` + `SectionLabel`, which phase 7 consumes instead of inferring |
| `Paragraph` otherwise | `IngestionDocumentParagraph` |
| paragraph order | reading order — no segmentation, no reading-order detector |
| `Table` | `IngestionDocumentTable` with `Cells`, row and column spans preserved — phase 8's detector becomes unnecessary on this path |
| `Figure` | `IngestionDocumentImage` with `Caption` from `figure.Caption.Content` and `CaptionSource = provider` |
| `BoundingRegions` | `PageNumber` and `BoundingBox`, in the same shape the heuristics write |

`PdfTextNormalizer` still runs: de-hyphenation and composition are about the text, not the layout, and the
service does not do them.

### 24.3 Figure bytes

**Resolved during implementation, better than this section first assumed.** The client exposes
`GetAnalyzeResultFigureAsync(modelId, resultId, figureId)`, so the service returns the figure image itself
once the request asks for `AnalyzeOutputOption.Figures`. There is no need to match XObjects by bounds, and
no need for a rasterizer: vector figures and figures assembled from several XObjects come back as one PNG,
which neither of the originally proposed options could manage.

A figure whose image cannot be downloaded keeps its caption and loses only its bytes, which demotes it to
the caption-only tier rather than failing the document.

### 24.4 Degrading

`FallbackIngestionDocumentReader` wraps the provider reader and the PdfPig reader. Anything that goes wrong
with the service — not configured, throttled, throwing, timing out, page limit exceeded — logs once and falls
through to PdfPig. A document is never rejected because a paid service was unavailable; that is the same rule
as 0.4's fifth.

Registration is explicit opt-in (`.AddAzureDocumentIntelligence()`), because keyed DI resolves the **last**
registration (A.4) and silently changing which reader serves `.pdf` would be a surprise.

### 24.5 Processors on this path

The processors must not undo what the reader established:

- `FigureCaptionProcessor` skips any image whose `CaptionSource` is already `provider`.
- `FigureSalienceProcessor` scores a provider caption at `PatternCaptionScore` — it is a real caption the
  service found, not a typographic guess.
- Phase 7's structure analyzer prefers a provider `SectionLabel` over an inferred one.

### 24.6 What it costs

Priced per page, so it wants a per-indexer switch rather than a global one, and a page cap. The figure
description budget (8.5) is unaffected; this replaces inference, not vision.

## Appendix A — Verified API reference

Everything here was obtained by reflection or by reading the package XML docs at
`%USERPROFILE%\.nuget\packages\…`. Members marked *verify* have a known name but an overload list
that must be confirmed in the XML file named before use.

### A.1 PdfPig 0.1.16 (`PdfPig` package; assemblies `UglyToad.PdfPig`, `.DocumentLayoutAnalysis`, `.Core`, `.Fonts`, `.Tokens`, `.Tokenization`)

XML docs: `…\pdfpig\0.1.16\lib\net8.0\UglyToad.PdfPig.xml` and `UglyToad.PdfPig.DocumentLayoutAnalysis.xml`.

```text
UglyToad.PdfPig.PdfDocument
  static Open(Stream)  static Open(byte[])  NumberOfPages  GetPage(int)  GetPages()  IsEncrypted  Version
  Information : DocumentInformation { Title, Author, Subject, Keywords, Creator, Producer, CreationDate, ModifiedDate }
  TryGetBookmarks(out Bookmarks)  TryGetXmpMetadata  Structure  Advanced
UglyToad.PdfPig.Content.Page
  Number  Width  Height  Size  Rotation  CropBox  MediaBox  Text  Letters : IReadOnlyList<Letter>  NumberOfImages
  GetWords()  GetWords(IWordExtractor)  GetImages() : IEnumerable<IPdfImage>  GetHyperlinks()  GetAnnotations()  GetMarkedContents()
  Paths  ExperimentalAccess : Page.Experimental { Paths : IReadOnlyList<PdfPath>, GetAnnotations(), GetOptionalContents() }
UglyToad.PdfPig.Content.IPdfImage
  Bounds : PdfRectangle  WidthInSamples  HeightInSamples  BitsPerComponent  RawBytes : Span<byte>  RawMemory : Memory<byte>
  TryGetPng(out byte[])  TryGetBytesAsMemory(out Memory<byte>)  ImageDictionary : DictionaryToken  IsInlineImage  IsImageMask
  ColorSpaceDetails  MaskImage  Interpolate  Decode  RenderingIntent
UglyToad.PdfPig.Content.Letter
  Value  PointSize  FontSize  FontName  Font : FontDetails  BoundingBox  GlyphRectangle  Location  StartBaseLine  EndBaseLine
  Width  TextSequence  TextOrientation  RenderingMode  Color  FillColor  StrokeColor
UglyToad.PdfPig.Core.PdfRectangle   Left  Bottom  Right  Top  Width  Height  Centroid  Area
UglyToad.PdfPig.Core.PdfPoint       X  Y

UglyToad.PdfPig.DocumentLayoutAnalysis
  WordExtractor.NearestNeighbourWordExtractor.Instance.GetWords(IReadOnlyList<Letter>) : IEnumerable<Word>
  PageSegmenter.DocstrumBoundingBoxes.Instance.GetBlocks(IEnumerable<Word>) : IReadOnlyList<TextBlock>
  PageSegmenter.RecursiveXYCut.Instance.GetBlocks(IEnumerable<Word>)
  PageSegmenter.DefaultPageSegmenter.Instance.GetBlocks(IEnumerable<Word>)
  ReadingOrderDetector.UnsupervisedReadingOrderDetector.Instance.Get(IReadOnlyList<TextBlock>) : IEnumerable<TextBlock>   (sets TextBlock.ReadingOrder)
  ReadingOrderDetector.RenderingReadingOrderDetector.Instance.Get(...)
  DecorationTextBlockClassifier.Get(IReadOnlyList<IReadOnlyList<TextBlock>> pagesTextBlocks, ...) : IReadOnlyList<IReadOnlyList<TextBlock>>   *verify overload*
  WhitespaceCoverExtractor.GetWhitespaces(...)   *verify*
  DuplicateOverlappingTextProcessor.Get(IReadOnlyList<Letter>)
  Distances.MinimumEditDistanceNormalised(string, string)   Distances.Euclidean(PdfPoint, PdfPoint)
  TextBlock { BoundingBox, Text, TextLines : IReadOnlyList<TextLine>, ReadingOrder, SetReadingOrder(int) }
  TextLine  { BoundingBox, Text, Words : IReadOnlyList<Word> }
  Word      { Text, BoundingBox, Letters, FontName }

UglyToad.PdfPig.Writer (fixtures)
  PdfDocumentBuilder()  AddStandard14Font(Standard14Font) : AddedFont  AddPage(PageSize) : PdfPageBuilder  AddPage(double w, double h)  Build() : byte[]
  PdfPageBuilder.AddText(string text, double fontSize, PdfPoint position, AddedFont font)   MeasureText(...)
  PdfPageBuilder.AddPng(byte[] pngBytes, PdfRectangle placement)  AddPng(Stream, PdfRectangle)
  PdfPageBuilder.AddJpeg(byte[] fileBytes, PdfRectangle placement)  AddJpeg(Stream, PdfRectangle)
  PdfPageBuilder.DrawLine(PdfPoint from, PdfPoint to, double lineWidth)
  PdfPageBuilder.DrawRectangle(PdfPoint position, double width, double height, double lineWidth, bool fill)
  PdfPageBuilder.SetTextAndFillColor(byte r, byte g, byte b)  SetTextRenderingMode(TextRenderingMode)
  Standard14Font.Helvetica / HelveticaBold / TimesRoman ...   PageSize.A4 (595 x 842)
```

### A.2 Microsoft.Extensions.DataIngestion 10.4.0-preview.1.26160.2

XML docs: `…\microsoft.extensions.dataingestion.abstractions\10.4.0-preview.1.26160.2\lib\net10.0\Microsoft.Extensions.DataIngestion.Abstractions.xml`.
See 1E for the type list. Constructors take a **markdown** string; `Text` must be assigned
separately. `IngestionDocumentImage.Content` is `ReadOnlyMemory<byte>?` (nullable struct):
assign `image.Content = bytes` and read with `image.Content is { } content ? content.ToArray() : null`.
`Metadata` is created lazily; check `HasMetadata` before enumerating in tests.

### A.3 Repository contracts this plan builds on (paths relative to `src/`)

```text
Abstractions/CrestApps.Core.AI.Abstractions/DataSources/IAIDataSourceSourceHandler.cs
  string SourceType
  ValueTask ValidateAsync(AIDataSource, ValidationResultDetails, ct)
  ValueTask<string> GetReferenceTypeAsync(AIDataSource, ct)
  IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(AIDataSource, ct)
  IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(AIDataSource, IEnumerable<string> documentIds, ct)
Abstractions/CrestApps.Core.Infrastructure.Abstractions/Indexing/Models/SourceDocument.cs        Title, Content, Fields : Dictionary<string, object>
Abstractions/CrestApps.Core.Infrastructure.Abstractions/Indexing/Models/DataSourceSearchResult.cs ReferenceId, Title, Content, ChunkIndex, ReferenceType, Score
Abstractions/CrestApps.Core.Infrastructure.Abstractions/Indexing/DataSources/IDataSourceContentManager.cs
  Task<IEnumerable<DataSourceSearchResult>> SearchAsync(IIndexProfileInfo, float[] embedding, string dataSourceId, int topN, string filter = null, ct)
  Task<long> DeleteByDataSourceIdAsync(IIndexProfileInfo, string dataSourceId, ct)
Abstractions/CrestApps.Core.Infrastructure.Abstractions/Indexing/ISearchDocumentManager.cs
  Task<bool> AddOrUpdateAsync(IIndexProfileInfo, IReadOnlyCollection<IndexDocument>, ct)   Task DeleteAsync(IIndexProfileInfo, IEnumerable<string> documentIds, ct)   Task DeleteAllAsync(...)
Abstractions/CrestApps.Core.Infrastructure.Abstractions/Indexing/ISearchIndexManager.cs
  Task<bool> ExistsAsync   string ComposeIndexFullName   Task CreateAsync(profile, IReadOnlyCollection<SearchIndexField>, ct)   Task DeleteAsync
Abstractions/CrestApps.Core.Infrastructure.Abstractions/Indexing/Models/SearchIndexField.cs        Name, FieldType (Text|Keyword|Integer|Float|DateTime|Vector), IsKey, IsFilterable, IsSearchable, VectorDimensions
Primitives/CrestApps.Core.Infrastructure/DataSourceConstants.cs                                  ColumnNames.{ReferenceId, DataSourceId, ChunkId, ChunkIndex, Title, Content, Embedding, Timestamp, ReferenceType, Filters}
Primitives/CrestApps.Core.AI/Indexing/DataSourceSearchIndexProfileHandler.cs                      BuildFields(int vectorDimensions)
Primitives/CrestApps.Core.AI/Services/DefaultAIDataSourceIndexingService.cs                       IndexDocumentsAsync (~268), BuildChunkIds (~488), TryCreateContextAsync (~385), EnsureKnowledgeBaseIndexAsync
Primitives/CrestApps.Core.AI/Services/IAIDataSourceIndexingQueue.cs
  ValueTask QueueSyncDataSourceAsync(AIDataSource)  QueueDeleteDataSourceAsync  QueueSyncSourceDocumentsAsync(profileName, ids)  QueueRemoveSourceDocumentsAsync
  QueueSyncDataSourceDocumentsAsync(string dataSourceId, IReadOnlyCollection<string> ids)  QueueRemoveDataSourceDocumentsAsync(string dataSourceId, IReadOnlyCollection<string> ids)
Primitives/CrestApps.Core.AI/Services/DataSourceRetrieval.cs                                      static Task<string> SearchAsync(IServiceProvider, DataSourceRetrievalRequest, string toolName, ILogger, ct); MaxQueries = 3; ReferenceCollector
Abstractions/CrestApps.Core.AI.Abstractions/Deployments/IAIDeploymentManager.cs                  ValueTask<AIDeployment> ResolveSlotAsync(slotName, deploymentName = null, clientName = null, fallbackDeploymentNames = null, ct)  GetAllBySlotAsync(slot, clientName, ct)  FindByNameAsync
Abstractions/CrestApps.Core.AI.Abstractions/Models/AIDeploymentSlotNames.cs                      Chat, Utility, Embedding, Image, Vision, SpeechToText, TextToSpeech, Realtime
Abstractions/CrestApps.Core.AI.Abstractions/Models/AIDeploymentFeatureNames.cs                   ImageInput = "imageInput", TextEmbedding, TextGeneration, ToolCalling, ...
Abstractions/CrestApps.Core.AI.Abstractions/Models/AIDeploymentMetadata.cs                       bool SupportsFeature(string)   (deployment.TryGet<AIDeploymentMetadata>(out var m))
Abstractions/CrestApps.Core.AI.Abstractions/Capabilities/IAIDeploymentCapabilityService.cs       SupportsFeatureOrUnconstrained(AIDeployment, string feature)  GetDeploymentsWithFeatureAsync  ResolveDeploymentWithFeatureAsync
Abstractions/CrestApps.Core.AI.Abstractions/Clients/IAIClientFactory.cs                          CreateChatClientAsync(AIDeployment)  CreateEmbeddingGeneratorAsync(AIDeployment)
Primitives/CrestApps.Core.AI.Documents/Services/IImageAnalysisService.cs                          Task<ImageAnalysisResult> AnalyzeAsync(Stream, string contentType, string fileName, string chatDeploymentName = null, ct)
Primitives/CrestApps.Core.AI.Documents/Models/ImageAnalysisResult.cs                              Caption, Description, OcrText, DetectedEntities, RawAnalysis, Success, Error; static Succeeded(...), Failed(error)
Primitives/CrestApps.Core.AI/AITemplateIds.cs                                                     ImageAnalysis = "image-analysis" (template at Primitives/CrestApps.Core.AI/Templates/Prompts/image-analysis.md, front-matter Title/Description/IsListable/Category)
Primitives/CrestApps.Core.AI.Documents/IDocumentFileStore.cs                                      Task<string> SaveFileAsync(fileName, Stream)  Task<Stream> GetFileAsync(fileName)  Task<bool> DeleteFileAsync(fileName)
Primitives/CrestApps.Core.AI.Documents/DocumentFileStoragePath.cs                                  static (StoredFileName, StoragePath) Create(referenceType, referenceId, fileName, subfolder = null)
Primitives/CrestApps.Core.AI.Documents/ServiceCollectionExtensions.cs                              AddCoreAIIngestionDocumentReader<T>(params ExtractorExtension[])  -> Configure<ChatDocumentsOptions> + TryAddSingleton<T> + AddKeyedSingleton<IngestionDocumentReader>(extension)
Primitives/CrestApps.Core.AI.Documents/Models/ExtractorExtension.cs                                Extension, Embeddable, IsTabular; implicit from string
Primitives/CrestApps.Core.AI.Documents/Models/ChatDocumentsOptions.cs                              AllowedFileExtensions, EmbeddableFileExtensions, TabularFileExtensions, MaxVisionImageBytesPerFile, AnalyzeImagesAtUpload
Abstractions/CrestApps.Core.AI.Abstractions/Models/WebCrawler.cs                                  DisplayText, AIDataSourceId, Enabled, ReindexIntervalMinutes, CreatedUtc, ModifiedUtc, Author, OwnerId (+ Source, Properties, ItemId)
Abstractions/CrestApps.Core.AI.Abstractions/Models/WebCrawlState.cs                                Url, LastModifiedUtc, ChangeFrequency, ContentHash, LastIndexedUtc, LastSeenUtc
Abstractions/CrestApps.Core.AI.Abstractions/DataSources/IWebCrawlerStore.cs                       : ISourceCatalog<WebCrawler>; GetByDataSourceIdAsync
Abstractions/CrestApps.Core.AI.Abstractions/DataSources/IWebCrawlStateStore.cs                    : ISourceCatalog<WebCrawlState>; DeleteByCrawlerIdAsync, DeleteByUrlsAsync
Primitives/CrestApps.Core.AI.WebCrawlers/Strategies/IWebCrawlerStrategy.cs                        Name; ValidateAsync; DiscoverAsync -> IReadOnlyList<CrawledPageRef>; FetchAsync -> CrawledPage
Primitives/CrestApps.Core.AI.WebCrawlers/ServiceCollectionExtensions.cs                           AddCoreWebCrawlers(): options, strategy resolver, keyed strategy, planner, reindex service, keyed source handler, keyed link resolver, catalog handler, hosted service, source descriptor
Primitives/CrestApps.Core.AI.WebCrawlers/Handlers/WebCrawlerCatalogHandler.cs                     CatalogEntryHandlerBase<WebCrawler>: Initializing/Updating -> PopulateAsync(JsonNode), Initialized/Creating set timestamps+owner, ValidatingAsync, DeletedAsync queues sync
Primitives/CrestApps.Core.AI.Mcp/McpResourceTypeHandlerBase.cs                                    ctor(string type); abstract GetResultAsync(McpResource, IReadOnlyDictionary<string,string> variables, ct); static CreateErrorResult(uri, message); static SanitizePath(path); static IsTextMimeType
Primitives/CrestApps.Core.AI.Mcp/McpServerBuilderExtensions.cs                                    three CallToolResult sites (~193, ~215, ~234); WithListResourceTemplatesHandler -> IMcpServerResourceService.ListTemplatesAsync
Primitives/CrestApps.Core.AI.Mcp/ServiceCollectionExtensions.cs                                    resource type handler registration helper (~210-222)
Abstractions/CrestApps.Core.AI.Abstractions/Tooling/IAIToolInstanceSource.cs                      AITool CreateTool(AIToolInstance)
Primitives/CrestApps.Core.AI/Tooling/Instances/DataSources/*.cs                                    DataSourceSearchToolInstanceSource, DataSourceSearchToolFunction : AIFunction (InvokeCoreAsync returns object), DataSourceSearchToolSettings { DataSourceId, RetrievalMode, TopNDocuments, Strictness, Filter }, AddDataSourceSearchSource(builder)
Abstractions/CrestApps.Core.Abstractions/ExtensibleEntity.cs (+ExtensibleEntityExtensions)         Properties : IDictionary<string, object>; Put<T>(), TryGet<T>(out), GetOrCreate<T>()
Utilities/CrestApps.Core.Support/JsonNodeExtensions.cs                                            TryUpdateTrimmedStringValue, TryGetBooleanValue, TryGetNullableInt32Value, ...
Primitives/CrestApps.Core.DataIngestion/HtmlIngestionDocumentReader.cs                            : IngestionDocumentReader; static Read(html, identifier), ExtractTitle, ExtractText
```

### A.4 Behaviours to remember while coding

- Keyed DI: `GetKeyedService<T>(key)` returns the **last** registration for that key.
- `IEnumerable<T>` DI resolution preserves **registration order** — the processor order relies on it.
- `AIFunction.InvokeAsync` returns `object`; anything not a `string` is stringified by callers
  that expect text — hence `ToString()` overrides on every rich result type.
- Readers and processors are **singletons**: no per-call mutable fields; take `IOptions<T>` and
  `TimeProvider` through the constructor.
- `ResolveSlotAsync` may return a deployment that does **not** have the slot's feature (documented
  "first capable" tail) — always check the feature.
- Azure AI Search rejects documents with undeclared properties; PostgreSQL adds columns lazily;
  Elasticsearch maps dynamically as `text` (not `keyword`) — never rely on dynamic mapping for a
  field you filter on.

## Appendix B — Catalog entry checklist

Adding a persisted catalog entry `T` (this plan adds exactly one: `KnowledgeObject`; the indexer
rename touches two existing ones) requires all of the following. Use `WebCrawler` /
`WebCrawlState` as the worked example for every item.

1. **Model** — `src/Abstractions/CrestApps.Core.AI.Abstractions/Models/T.cs`: `sealed`, derives
   `SourceCatalogEntry` (or `CatalogItem`), implements `IModifiedUtcAwareModel` when it has
   `ModifiedUtc`, `ICloneable<T>` with a full `Clone()`. XML docs on every property.
2. **Store interface** — `…/DataSources/ITStore.cs : ISourceCatalog<T>` with the query methods.
3. **Catalog handler** — `CatalogEntryHandlerBase<T>` (`WebCrawlerCatalogHandler` is the template):
   `InitializingAsync`/`UpdatingAsync` → `PopulateAsync(model, JsonNode)` mapping **every**
   settable property with `JsonNodeExtensions` helpers; `InitializedAsync`/`CreatingAsync` set
   `CreatedUtc`/`ModifiedUtc` from `TimeProvider` and `Author`/`OwnerId` from the current user;
   `ValidatingAsync` fails on missing required fields with `ValidationResult(message, [nameof(...)])`.
   Registered with `TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<T>, THandler>())`.
4. **EntityCore store** — `src/Stores/CrestApps.Core.Data.EntityCore/Services/EntityCoreTStore.cs : SourceDocumentCatalog<T>, ITStore`;
   add a `case T` in `Services/CatalogRecordFactory.cs` (denormalize the owner id into
   `record.ReferenceId` as the crawler does); register in `ServiceCollectionExtensions.AddCore…StoresEntityCore()`
   with `Replace(ServiceDescriptor.Scoped<ITStore, EntityCoreTStore>())` plus `ICatalog<T>` / `ISourceCatalog<T>` forwards.
5. **YesSql store** — `src/Stores/CrestApps.Core.Data.YesSql/Services/YesSqlTStore.cs : DocumentCatalog<T, TIndex>, ITStore`;
   `Indexes/…/TIndex.cs : CatalogItemIndex` + `TIndexProvider : IndexProvider<T>`;
   `TIndexSchemaBuilderExtensions.CreateTIndexSchemaAsync`; call it from
   `src/Startup/CrestApps.Core.Mvc.Web/Services/YesSqlServiceCollectionExtensions.cs` via `TryCreateTableAsync`.
6. **DI in the feature project** — default in-memory/no-op registration is not needed; the
   feature's `AddCore…()` registers the handler and expects a store to be provided by one of the
   store packages (as `AddCoreWebCrawlers` does).
7. **Tests** — handler `PopulateAsync` round-trip for every property; validation failures; store
   tests follow `tests/CrestApps.Core.Tests/EntityCoreStoreTests.cs`.
8. **Docs** — mention the new record and its store registration in `core/data-storage.md`.

## Appendix C — Glossary

- **Ingestion pipeline** — reader + ordered processors producing an `IngestionDocument` (4).
- **Processor** — an `AIDocumentIngestionProcessor` that enriches elements in place (6, 7, 8).
- **Knowledge object** — one typed unit (document, article, text, figure, chart, table) with a
  canonical id, stored in `IKnowledgeObjectStore` and projected into the knowledge base (11).
- **Knowledge base** — the data source's vector index (`AIKnowledgeBaseIndexProfileName`).
- **Pre-chunked row** — a `SourceDocument` whose `Content` is already ≤ one chunk (13.3).
- **Indexer** — a configured connector + schedule + model choices pointing at one data source (17).
- **Connector** — discover/fetch for one kind of location (16).
- **Tier** — `Skip` / `CaptionOnly` / `Describe` salience outcome per image (7).
- **Value confidence** — `Exact` / `AxesOnly` / `Descriptive` for chart data (11.4).
