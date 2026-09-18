---
sidebar_label: Document Processing
sidebar_position: 6
title: Document Processing
description: Configure document ingestion, search, downloads, and tabular workflows for AI experiences.
---

# Document Processing

> Add the document layer that lets your AI features read uploads, answer questions with citations, and work with spreadsheets and CSV files.

`CrestApps.Core.AI.Documents` provides the document features used by chat and orchestration experiences. It is the package to add when you want file uploads to become useful AI context instead of plain attachments.

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

## What Document Processing Handles

At a high level, document processing lets your app:

- accept uploaded files for AI features
- extract usable content from supported formats
- make document content searchable in conversations
- route spreadsheets and CSV files through a structured tabular workflow
- expose uploaded and generated files as downloads in chat

## The ingestion path on its own

Turning a file into knowledge and answering chat questions about an upload are two different jobs, and they
live in two packages.

`CrestApps.Core.AI.Ingestion` holds the first one: the readers that turn a file into a document, the processor
pipeline that captions and describes its figures, and `IKnowledgeIngestionService`, which stores the result as
the typed objects of an AI data source. `CrestApps.Core.AI.Documents` holds the second: uploads, tabular
workspaces, generated files and the chat tool surface.

`AddCoreAIDocumentProcessing()` calls `AddCoreAIDocumentIngestion()` for you, so a host that wants the chat
experience carries on as before. A host that only reads files into a knowledge base registers the ingestion
half on its own and none of the chat services come with it:

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIDocumentIngestion(ingestion => ingestion
        .AddPlainTextReader()
        .AddFigureProcessing()
        .AddFigureBackfill()
        .AddPdf()
    );
```

Only what cannot be left out is registered outright — the pipeline, the file store, and
`IKnowledgeIngestionService`. Everything else is asked for:

| Call | Registers | Leave it out when |
| --- | --- | --- |
| `AddPlainTextReader()` | the reader for `.txt`, `.csv`, `.md`, `.json`, `.xml`, `.html`, `.htm`, `.log`, `.yaml`, `.yml` | those file types never reach you |
| `AddFigureProcessing()` | figure captioning, salience, and vision transcription | you ingest text you know has no figures, and want no vision calls |
| `AddFigureBackfill()` | the hosted job that finishes figures an ingest left pending | you run ingests elsewhere, or want no background work in this process |
| `AddPdf()` | the PDF reader, from `CrestApps.Core.AI.Ingestion.Pdf` | you read no PDFs |

:::note
`AddPdf()` on the **ingestion** builder registers the PDF *reader* only. `AddPdf()` on the document processing
builder registers that same reader plus the writer that turns generated content into a downloadable PDF,
which needs document processing. Reading a PDF and writing one are separate packages.
:::

In practice a file source host rarely calls this directly, because `AddCoreFileSources()` already registers
the reader, the figure processors and the backfill — see [File Sources](../data-sources/indexers.md).

:::note
If you want both, call `AddDocumentProcessing()` **before** `AddCoreFileSources()`. Keyed readers resolve to
the last registration, and the HTML reader file sources register has to win over the plain-text reader the
ingestion path registers for `.html`, or a web page read from a folder or a server is indexed with its markup.
:::

## Built-In Capabilities

### Document-aware chat

Users can upload documents and ask natural-language questions about them. The AI can answer using the uploaded content instead of relying only on the model's general knowledge.

### Downloadable references

When enabled, document references in chat become clickable downloads. This is useful both for original uploads and for files the AI generates during the conversation.

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
    .AddDownloadAIDocumentEndpoint()
    .AddDownloadAIDocumentFigureEndpoint();
```

The second endpoint serves the figures read out of an uploaded document, which is what lets an answer show a chart from a PDF rather than describe it. See [Showing a figure in the chat](#showing-a-figure-in-the-chat).

### Tabular data workflows

CSV and Excel files are treated as structured data. This is the right fit for tasks such as cleaning up rows, filling missing values, filtering results, and exporting an updated file back to the user.

### Optional format support

Add the readers you need:

- `AddOpenXml()` for Office documents
- `AddPdf()` for PDF files
- `AddMarkdown()` for Markdown-aware text handling

## Storage Choices

Register one of the supported store stacks on the document processing builder.

### Entity Framework Core

```csharp
.AddDocumentProcessing(documentProcessing => documentProcessing
    .AddEntityCoreStores()
    .AddOpenXml()
    .AddPdf()
    .AddReferenceDownloads()
)
```

### YesSql

```csharp
.AddDocumentProcessing(documentProcessing => documentProcessing
    .AddYesSqlStores()
    .AddOpenXml()
    .AddPdf()
    .AddReferenceDownloads()
)
```

Uploaded files are stored through `IDocumentFileStore`. The default local file storage can be replaced when you want a different backend.

```csharp
builder.Services.AddSingleton<IDocumentFileStore, AzureBlobDocumentFileStore>();
```

## Upload Configuration

Use `ChatDocumentsOptions` to align upload validation with the formats your app supports.

```csharp
services.Configure<ChatDocumentsOptions>(options =>
{
    options.Add(".rtf", embeddable: true);
    options.Add(".tsv", embeddable: false);
});
```

Use these values in both the upload UI and server-side validation so the supported-format guidance stays consistent.

### How large an upload may be

`InteractionDocumentSettings.MaxIndexableCharacters` caps the **extracted text** a document may hold and still be indexed. It counts characters of text, not bytes on disk, because that is what decides whether the document can be embedded — a 40 MB scanned PDF may carry less text than a 200 KB spreadsheet.

A document over the cap is **refused at upload**, with a message naming the file, its measured size and the configured limit. It is not accepted and quietly left unsearchable, which is what used to happen: the file appeared attached, the chat listed it, and every search over it returned nothing.

The measuring pass runs before ingestion and with figure description off, so an oversized document is refused in seconds rather than after paying for a vision call per figure. That means the measured size is the text without figure descriptions, and is a little smaller than the document's final indexed length.

| value | meaning |
|---|---|
| a positive number | the cap, in characters of extracted text |
| `0` | no limit — every document is indexed however long it is |
| default | `50000` |

An AI profile may override the site value through `DocumentsMetadata.MaxIndexableCharacters`, where `null` means "use the site setting". An AI template carries the same field, so a profile created from a template starts with it.

:::warning
`0` means **unlimited**, not "refuse everything". A host that sets it is accepting that a large document is embedded in full.
:::

### Whether figures are described

Describing figures is the expensive part of ingestion — one vision call per figure, which is why a magazine takes minutes to ingest and a text file takes seconds. `InteractionDocumentSettings.DescribeFiguresInUploads` turns it off for uploaded documents without turning off figure extraction: a figure is still pulled out, still stored and still servable, and it keeps its caption. Only the transcription is skipped.

It defaults to `true`, and an AI profile may override it through `DocumentsMetadata.DescribeFiguresInUploads`, where `null` means "use the site setting".

The setting maps to `FigureProcessingMode.Auto` when on and `FigureProcessingMode.Off` when off. Description needs a deployment in the **Vision** slot — not the utility model. With none configured nothing is described whatever this is set to, so leaving it on costs nothing on a host that has no vision deployment.

These two settings only apply to documents uploaded into a chat session or a chat interaction. A file source ingesting from a folder or a server carries its own `FigureMode` and `MaxFigureDescriptionsPerDocument` on the record, covered in [File data sources](../data-sources/file.md).

## Custom File Formats

If you need to support a format that is not built in, register a custom reader for the new extension.

```csharp
builder.Services.AddCoreAIIngestionDocumentReader<MyCustomReader>(".custom", ".myformat");
```

This lets you extend document support without changing the rest of the document-processing setup.

## Reading Structure From A Service

Everything above infers structure from where the glyphs sit. Where an operator has a document-understanding
service, that inference can be replaced with fact. `AddDocumentIntelligence()` registers a reader backed by
Azure AI Document Intelligence:

```csharp
.AddDocumentProcessing(documentProcessing => documentProcessing
    .AddPdf()
    .AddDocumentIntelligence(options =>
    {
        options.Endpoint = "https://my-resource.cognitiveservices.azure.com/";
        options.ApiKey = "...";   // omit to use the ambient Azure credential
    })
)
```

The layout model reports, for any language, which paragraph is a heading, which is a running head, what
order the page reads in, where the tables are and what their cells span, and which caption belongs to which
figure. Figure images are downloaded from the service, so vector figures and figures assembled from several
objects arrive as ordinary images.

Three things make this safe to depend on:

- **Registration is explicit.** A keyed reader resolves to the last registration, so adding the package
  cannot silently change which reader serves every PDF in the application.
- **It degrades.** The reader is wrapped in a `FallbackIngestionDocumentReader`. Unconfigured, throttled,
  over its page limit, timed out or simply down - it logs once and the local reader produces the document
  instead. A document is never rejected because a paid service was unavailable.
- **Nothing overwrites it.** A caption the service reported is marked `provider`, and the caption processor
  leaves those figures alone rather than replacing a printed caption with a guess. Salience scores a
  provider caption as strongly as a printed numbered one, because that is what it is.

The service is priced per page, so `MaxPages` caps what is worth sending; a longer document falls back to
local extraction. Text repair still runs on this path - de-hyphenation and composition are about the text,
not the layout, and the service does not do them.

## The Ingestion Pipeline

Every upload goes through `IAIDocumentIngestionPipeline`: it picks the reader for the file, reads it into an `IngestionDocument`, and then runs each registered processor over the result. A processor enriches the document in place — resolving captions, scoring images, describing figures — so a reader stays responsible for reading and nothing else.

Register your own processor with:

```csharp
builder.Services.AddCoreAIIngestionDocumentProcessor<MyProcessor>();
```

Processors run in **registration order**, so a processor registered before `AddDocumentProcessing()` runs ahead of the built-in ones. Derive from `AIDocumentIngestionProcessor` to receive the per-run `DocumentIngestionContext` alongside the document; the library's own single-argument `ProcessAsync` overload keeps working and runs with `DocumentIngestionContext.Default`.

An enrichment step must never fail an ingest. When a processor cannot do its work — no model is configured, a heuristic is unsure — it should log once and leave the document as it found it.

## PDF Layout Analysis

PDF pages are segmented into blocks and put into reading order, so a two-column article reads down one column and then the other rather than across both. Blocks that repeat at the edge of every page — running heads, footers, page furniture — are classified as decoration: they are kept on the page as header and footer elements marked `ElementMetadataKeys.IsDecoration`, and everything that embeds or displays text skips them. They are kept rather than dropped because structure analysis reads them — a running head says what kind of section a page belongs to, and a printed page number says what folio a citation should name.

Decoration classification is a heuristic, so it is guarded: a block is never treated as decoration when it is the only block on its page, when it is longer than `MaxDecorationCharacters`, or when it reads as more than one sentence.

Extracted text is then repaired, in ways that apply to any script rather than to English:

- **Words broken across a line are rejoined.** This is the single largest source of unsearchable words in a typeset document, and it is worst in the languages that build long compounds. A hyphen only counts as a break when a line break follows it; a compound that continues in capitals keeps its hyphen.
- **Ligatures and presentation forms are expanded** to the letters they stand for — Latin, Armenian and Arabic — so a word set with an "fi" ligature is found by a search for its plain spelling. The stray space some producers emit after a ligature is removed.
- **Accents are composed.** Extraction often produces decomposed text, where a word looks identical on screen and compares unequal. Composing with `FormC` fixes that and, unlike `FormKC`, leaves numbers alone.
- **Invisible formatting characters are dropped** — zero-width spaces, byte order marks, bidirectional controls. The zero-width non-joiner and joiner are deliberately kept: they look invisible but are letters of the word in Persian and the Indic scripts.
- Runs of whitespace collapse to one space, or to one line break when the run contained one. The line breaks survive because they carry structure the words alone do not: a table of contents is one block whose every line is a title and a page number, and it can only be read line by line. Digits, decimal commas and decimal points are never touched, so a printed coefficient survives verbatim.

Sentence detection, which the decoration guard and the in-text reference search both use, goes through `TextSegmentation`. It knows the terminators of scripts that do not end sentences with a full stop, and it does not require a capital letter to start the next sentence — there is no such thing in Arabic, Hebrew, CJK, Thai or the Indic scripts, so a rule keyed on capitalisation would simply never fire there.

Tune any of this with `PdfLayoutOptions`:

```csharp
services.Configure<PdfLayoutOptions>(options =>
{
    options.StripDecoration = true;               // classify repeated running heads and footers
    options.MaxDecorationCharacters = 200;        // never treat a longer block as decoration
    options.EmitDecorationAsHeaderFooter = true;  // false drops decoration instead of marking it
    options.UseLayoutAnalysis = true;             // false falls back to raw content-stream text per page
    options.EmitImages = true;                    // false emits no figures from placed images
    options.MinImageSamples = 32;                 // smaller than this is a rule, a bullet or an icon
    options.EmitTables = true;                    // false reads a ruled table as prose
    options.EmitVectorFigures = true;             // false misses every chart the page draws rather than places
    options.MaxVectorSegmentsPerPage = 4000;      // skip drawn-table and figure detection on denser pages
});
```

Two of those are easy to underestimate. `EmitTables` decides whether a ruled table is read as a table at all — read as prose it is a row of numbers with nothing saying which column each belongs to. `EmitVectorFigures` decides whether figures the page **draws** are read, as opposed to images it **places**: a chart produced by a spreadsheet is usually not an image, just instructions for drawing one, so reading only placed images misses every chart of that kind.

`MaxVectorSegmentsPerPage` exists because grouping drawn lines into grids and drawings compares every line with every other. A page of dense vector artwork — a map, an advertisement drawn as geometry — can carry tens of thousands, and reading it would take minutes for figures nobody asked about. Past the ceiling the page keeps its text, its placed images and any whitespace-aligned tables, and simply reports no drawn tables or figures.

Setting `UseLayoutAnalysis = false` reproduces the pre-layout-analysis behaviour exactly: one paragraph per page holding the raw content-stream text. It is the escape hatch if segmentation misbehaves on a particular corpus.

You rarely need that escape hatch to avoid a failure, because layout analysis is treated as an improvement on reading the text rather than a precondition for it. A page whose glyph geometry defeats the segmenter keeps its text, unsegmented, and loses only its structure; a failure that takes down the whole analysis pass falls through to the raw reader for the entire document. Either way the content is still read, and the reason is logged once as a warning. A PDF that is encrypted or genuinely malformed still fails the ingest, and says so.

## Figures

A PDF's figures are read out alongside its text. Each one carries its bytes, its page, its bounds and a hash of its content; artwork placed twice is recorded once and the repeat points back at it, so the repeat is neither stored nor turned into a second knowledge object. `MinImageSamples` (default 32) keeps rules, bullets and icons out.

### Captions

`FigureCaptionProcessor` works out which caption belongs to which figure and what body text gives it meaning. It calls no model.

Caption direction is **not** a fixed rule. The same publication routinely prints figure captions below the artwork and table captions above it, so the processor reads the document's own habit from the captions it is sure about — a numbered caption with exactly one figure beside it, and that figure with exactly one caption beside it — and only falls back to the configured default when the document has not shown enough of them.

Caption patterns are data. The defaults cover English, German, Dutch, the Scandinavian languages, French, Italian, Spanish, Portuguese, Polish, Czech, Russian, Ukrainian, Hungarian, Japanese, Chinese and Korean. That list is not cosmetic: a caption matching no pattern falls back to the typography heuristic, which scores below the threshold a figure must clear to be transcribed, so a missing language means figures stored with a caption and no description. Add your own without forking anything:

```csharp
services.Configure<CaptionPatternOptions>(options =>
{
    options.Patterns.Add(new CaptionPattern
    {
        Expression = new Regex(@"^Abbildung\s*\d+", RegexOptions.IgnoreCase),
        Bucket = CaptionBuckets.Figure,
    });
});
```

A figure printed with no caption falls back to the sentence in the prose that refers to it by number. If an unusual layout defeats the defaults entirely, replace `IFigureCaptionCandidateDetector` or `IFigureCaptionResolver` rather than the processor.

### Salience

`FigureSalienceProcessor` decides which figures are worth keeping and which are worth a model call. Most images in a publication are logos, advertising artwork or decoration; a handful carry the whole answer to a question the text cannot answer.

Each figure is scored from signals that cost nothing to compute — and lands in one of three tiers, recorded on the figure:

| Signal | Effect |
|---|---|
| Caption matched a numbered pattern | `PatternCaptionScore` (+3) |
| Caption recognized only by its typography | `TypographyCaptionScore` (+2) |
| Mentioned by number in the body text | `CitedInProseScore` (+2) |
| Same artwork on three or more pages | `RepeatedArtworkPenalty` (−4) |
| Smaller than 150 samples, or aspect beyond 6:1 | `SmallOrExtremePenalty` (−2) |
| Covers a page that carries almost no text | `FullBleedPenalty` (−3) |

A printed, numbered caption and "this block was in smaller type" are deliberately not worth the same. Weighting them equally promoted every stretch of fine print beside a photograph into the same tier as a labelled chart.

| Tier | Meaning |
|---|---|
| `Skip` | Dropped. The bytes are never stored. |
| `CaptionOnly` | Kept and indexed by its caption. No model call. |
| `Describe` | Worth transcribing, because it carries information the text does not. |

`DocumentIngestionContext.FigureMode` overrides the scoring: `Off` drops every figure, `All` promotes everything that was not dropped. `MaxFigureDescriptionsPerDocument` caps how many figures one document may have described; the surplus is demoted to `CaptionOnly`, never dropped. Tune the rest with `FigureSalienceOptions`.

A **scanned document** is the one case where none of those signals apply. It has no text layer: every page is one image and nothing captions or cites it, so every signal above would drop it and nothing would be indexed at all. When at least `ScannedDocumentPageRatio` (default one half) of a document's pages carry no text beyond `ScannedPageMaxCharacters` (default 40), a full-page image on such a page scores `ScannedPageScore` (default 3) and lands in the describe tier, because transcribing the page is the only way the document ever becomes answerable. A single full-bleed, text-less page in a document that otherwise has text is still an advertisement.

Salience deliberately does **not** look at the figure's pixels. An earlier design scored colour counts and straight-line runs to tell a chart from a photograph, which meant decoding the image. Measured against a real trade magazine, four figures in five were JPEG, so those two signals reached a fifth of the figures and decided nothing; what actually separates a figure from advertising artwork is whether the document said anything about it.

### Transcription

`FigureDescriptionProcessor` is the only processor that calls a model. For each figure salience marked `Describe`, it sends the image, its caption, the surrounding prose and the document's language to a vision deployment through `IImageAnalysisService`, and stores what comes back as the figure's alternative text.

The prompt (`figure-transcription`) asks for a **literal transcription**, not an interpretation: chart type, axis titles and units, every tick label, every legend entry, every printed number and equation verbatim, then the trend in one sentence. It is told never to translate and never to state a value that is not printed. A figure whose series carry no printed labels gets its shape described and its values left alone — a confident wrong number is worse than no number.

Nothing here can fail an ingest:

- no vision deployment configured, or one that turns out not to accept images: logged once at Information, the document continues as text
- a call that throws: logged at Warning, that one figure drops back to `CaptionOnly`, the rest are still transcribed
- `MaxFigureDescriptionsPerDocument` is enforced again here as a hard stop on calls made

For an uploaded document this whole step is skipped when [figure description is turned off](#whether-figures-are-described). The figure is still extracted, stored and servable, and still carries its caption; what it loses is the transcription, so a value printed only inside the artwork is no longer searchable text.

Slot resolution falls back to "the first deployment capable of the slot's feature", so a non-null answer is not proof that the deployment accepts images. The capability is always checked.

Transcriptions are cached by content hash and prompt version through `IFigureDescriptionCache` (in-memory by default), so the same artwork is never described twice. The prompt version is part of the key deliberately: a transcription produced by an older prompt is not what the current prompt would produce, and serving it would hide a template change behind a cache. Bump `FigureDescriptionProcessor.FigureTranscriptionPromptVersion` whenever the template changes.

`IImageAnalysisService` gained an overload taking `ImageAnalysisRequest`, which carries the caption, the surrounding text, the language, the prompt template and the deployment. The original stream overload still works and delegates to it.

### How a figure reaches the text

A figure that has something to say is flattened as a fenced block, so a chunk boundary can never separate a description from the figure it describes:

```text
[figure report.pdf-p8-1 | page 8 | Figure 3. Measured values against the model.]
{the description, once a vision model has transcribed it}
[/figure]
```

The caption paragraph itself is not emitted a second time, and if a chunker splits the block anyway the opening line is repeated at the top of the continuation. Figure bytes are written through `IDocumentFileStore` under the owning document, and the list of figures is recorded on the `AIDocument` as a `DocumentFigureList`.

### Showing a figure in the chat

The text tells the model a figure exists and what was read off it; the picture reaches the conversation through two pieces that a host registers together:

- `AddDownloadAIDocumentFigureEndpoint()` serves `ai/documents/{documentId}/figures/{figureId}`. It applies the same authorization as the document download — whoever may manage the owning chat interaction, session or profile may see its figures — and only ever serves a picture the document actually lists.
- the `view_document_figure` system tool takes a document ID and the figure ID from a figure block and returns the caption, the page and the link, together with the markdown image to embed (`![caption](link)`). Passing a `question` makes a vision deployment look at the picture and answer it, for the cases where the stored transcription does not say what the model needs. `get_document_metadata` lists a document's figures so the model can discover them.

```csharp
app.AddChatApiEndpoints()
    .AddDownloadAIDocumentEndpoint()
    .AddDownloadAIDocumentFigureEndpoint();
```

Tool results that render themselves — `DataSourceRetrievalResult`, `KnowledgeObjectToolResult` and anything else implementing `IAIToolContentProvider` — reach a chat model as their text. The function-invoking client would otherwise serialize them as JSON, handing the model an envelope around its text or a picture as a base64 string many kilobytes long; the rich shape is kept for transports that can use it, such as the MCP server.
