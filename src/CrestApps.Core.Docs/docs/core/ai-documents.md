---
sidebar_label: AI Documents
sidebar_position: 14
title: AI Documents
description: Add document uploads, search, citations, image understanding, and tabular file workflows to AI conversations.
---

# AI Documents

> Let users upload files and ask questions about them in chat, with citations, downloads, and built-in support for text, images, and tabular data.

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

## What It Gives You

With AI Documents enabled, your users can:

- Upload knowledge files for chat, profiles, or templates
- Ask questions about uploaded content and get cited answers
- Search document content semantically instead of by exact keyword only
- Work with spreadsheets and CSV files through a tabular workflow
- Create, convert, edit, preview and analyse PDF files through the PDF Agent
- Download generated files such as exports and AI-authored documents
- Include supported images in chat flows

## Supported Experiences

### Text and knowledge files

For text-heavy files such as Markdown, text, Word, PDF, HTML, JSON, or XML, CrestApps.Core extracts the useful content and makes it available during conversation. This is the main path for summaries, Q&A, reviews, rewrites, extraction, and similar knowledge tasks.

### Tabular files

CSV and Excel uploads are handled as structured data instead of plain text. That means the AI can filter, update, reshape, and export rows without asking the model to copy large tables into the prompt.

Multi-sheet Excel workbooks are supported: each worksheet is loaded as its own queryable table, and column value types — numbers (including currency and thousands-separated amounts) and dates — are detected from the row data rather than the spreadsheet cell format, so the AI can aggregate, sort, and filter accurately.

This is the recommended path for tasks such as:

- filling blank cells
- filtering rows
- adding calculated columns
- exporting an updated spreadsheet for download

Under the hood, tabular workflows are handled by the built-in **Tabular Data Agent**. It is a code-defined, always-available **system agent** that stays hidden from the AI Profile and Chat Interaction agent pickers, yet still participates in orchestration and is exposed through the A2A host for remote clients.

#### Seeing the data

Everything else the agent does answers a question *about* the data. The hidden `preview_tabular_data` tool shows the data itself: a picture of the sheet, with its header row, lettered columns, numbered rows and numbers aligned right, drawn from the first rows and columns of whatever is currently loaded.

It is what lets a user check for themselves that the right worksheet was read and the header row was found where they expect it, rather than taking the answer's word for it. The agent calls it whenever the user asks to see, view or preview a table, once after the first question about a newly uploaded file, and — given a `sql` argument — to show what an export will contain before the file is created.

The picture is written as SVG and served through the same authorized document download endpoint as any other generated file, so it needs no drawing library, no fonts on the host, and no extra registration. The tool returns one `[fig:N]` marker per table, which the chat surfaces replace with the picture.

Previews are bounded on purpose and say what they left out — *"Showing 50 of 4,812 rows and 12 of 20 columns"* — because a grid large enough to hold everything is scaled down by the chat surface until none of it is readable. The limits are configurable:

```csharp
builder.Services.Configure<TabularPreviewOptions>(options =>
{
    options.MaxRows = 50;
    options.MaxColumns = 15;
    options.MaxCellCharacters = 32;
    options.MaxTables = 4;
    options.MaxImageWidth = 1100;
});
```

A host with no document download endpoint registered cannot serve the picture, and one with no writer registered for the preview format cannot write it; in either case the tool falls back to a Markdown table of the same window of the same data. The user can also ask for the text form directly.

The preview format is registered as a writer the host can resolve but **not** as a format a caller may request, so `generate_file` and `export_tabular_data` will not produce one. A preview is markup this host generated and escaped; the same extension reached through a tool whose `content` argument is written verbatim would serve model-supplied markup from your origin.

#### Spreadsheet formatting

Exported `.xlsx` files are written with real cell types: a numeric column becomes numbers and a date column becomes date serials, so the recipient can sum, sort, filter, and chart the result rather than receiving a sheet of text. A column whose leading zeros matter — a postal code or an account number — is detected and kept as text.

On top of that, the agent's hidden `format_tabular_data` tool records how the exported workbook should look, and `export_tabular_data` applies whatever is currently recorded:

| Capability | What the user can ask for |
| --- | --- |
| Number formats | currency, accounting, number, percent, date, date/time, time, duration, scientific, text, or an explicit format code, with decimal places, a currency symbol, and negatives in red |
| Cell styling | bold, italic, underline, font name and size, text color, fill color, alignment, text wrapping, borders, column widths |
| Sheet layout | worksheet name, styled header row, frozen header, filter dropdowns, banded rows |
| Conditional formatting | gradient color scales, data bars, icon sets, duplicate highlighting, and comparisons (greater than, less than, equal to, between, contains text) |
| Formulas | calculated columns written as live formulas that reference other columns by name (`={Actual}-{Planned}`), and a total row built from `SUBTOTAL` so it follows the reader's filtering |
| Charts | column, bar, line, pie, and area charts embedded in the worksheet and bound to its cell ranges, so they redraw when the data changes |

The recorded formatting is stored alongside the workspace data, so formatting requested in one turn still applies when the file is exported in a later one, and a follow-up request refines it instead of replacing it.

Requires `AddOpenXml()`; other formats (such as CSV) ignore the formatting and export the data alone.

#### Charts in the conversation

A chart embedded in the workbook is for the downloaded file. To render a chart in the chat itself, the agent calls the `generate_chart` system tool with the actual values (`labels` and `series`), which builds the chart configuration directly from those numbers.

### PDF files and the PDF Agent

`AddPdf()` registers the PDF reader, the PDF writer behind generated files, and the built-in **PDF Agent**
(`pdf-agent`). Like the Tabular Data Agent it is a code-defined **system agent**: always available to the
primary model, exposed through the A2A host, and hidden from the agent pickers. The primary model delegates
every request that produces or works on a PDF to it, and every follow-up as well ("now add page numbers",
"make the title blue").

#### The workspace

Each chat session or chat interaction has a PDF workspace that persists between turns:

- **Uploaded PDFs are never changed.** The first edit of `contract.pdf` saves a working copy named
  `contract`; later edits change that copy.
- **Composed PDFs** are kept as a definition — page setup, theme, running heads, cover page, table of
  contents and numbered content blocks — and rendered on demand, so a follow-up changes one block or one
  setting instead of rebuilding the document.
- The workspace is stored through `IDocumentFileStore` under `documents/pdf-workspaces/`, and is removed
  when the conversation is deleted or its history is cleared.

#### What the agent can do

| Area | Tools |
| --- | --- |
| Authoring | `create_pdf`, `add_pdf_content` (headings, paragraphs with inline Markdown, lists, tables with number formats, totals and highlighted cells, images, charts, callouts, key-value facts, quotes, code, page breaks, signature lines), `format_pdf` (page size and margins, theme colours and fonts, logo, header and footer, page numbers, cover page, table of contents, watermark, metadata, PDF/A-2B), `preview_pdf`, `export_pdf` |
| Conversion | `convert_to_pdf` (Word, PowerPoint, spreadsheets and CSV, Markdown, HTML, text and images — several files into one PDF), `convert_from_pdf` (Word, Excel, CSV, Markdown, HTML, text, JSON, SVG pages) |
| Pages | `edit_pdf_pages`: merge, extract, split, reorder, reverse, rotate, delete, duplicate, insert blank pages, crop, resize, watermark, stamp, page numbers, header and footer |
| Content and review | `edit_pdf_content` (replace text in place, add text and images, remove or cover areas), `manage_pdf_annotations` (notes, highlights, underlines, strikeouts, shapes, arrows, text boxes, stamps), `add_pdf_bookmarks`, `add_pdf_links`, `edit_pdf_metadata`, `manage_pdf_attachments`, `manage_pdf_layers`, `flatten_pdf` |
| Forms | `get_pdf_form_fields`, `fill_pdf_form`, `edit_pdf_form`, `validate_pdf_form` |
| Security | `redact_pdf` (true redaction, verified by reading the result back), `find_pdf_sensitive_data`, `sanitize_pdf`, `protect_pdf`, `remove_pdf_security`, `sign_pdf`, `verify_pdf_signature` |
| Reading | `get_pdf_info`, `extract_pdf_text`, `extract_pdf_tables`, `extract_pdf_images`, `extract_pdf_structure`, `extract_pdf_links`, `search_pdf`, `get_pdf_page_content`, `analyze_pdf_layout`, `detect_pdf_language`, `generate_pdf_outline`, `compare_pdfs` |
| Understanding | `ocr_pdf`, `analyze_pdf_images`, `summarize_pdf`, `ask_pdf` (page-cited passages and answers), `extract_pdf_entities`, `extract_pdf_data`, `classify_pdf`, `cross_reference_pdfs` |
| Quality | `validate_pdf`, `check_pdf_quality`, `check_pdf_accessibility`, `tag_pdf_accessibility`, `validate_pdf_compliance` (PDF/A-1, -2, -3 and PDF/UA-1), `optimize_pdf` |

The understanding tools use the utility deployment for text and the vision deployment for scanned pages and
pictures; on a host without one they say so instead of guessing.

A converted file carries its content in the PDF's theme — headings, formatted text, links, lists, tables,
pictures, a page per slide — not a copy of its page design. A spreadsheet converted in a conversation that
has a tabular workspace keeps the number formats and colours the tabular preview shows.

#### Previews

`preview_pdf` shows pages in the chat as pictures. Like the tabular preview they are SVG — drawn from the
page's own text, paths and images, so no rasterizer or fonts are needed on the host — stored through
`IGeneratedDocumentService`, and returned as `[fig:N]` markers. A preview is bounded, says which pages it left
out, and falls back to the pages' text on a host that cannot serve pictures. The preview format is
resolvable but not requestable, so `generate_file` cannot produce one.

#### Generated PDFs

The same composer writes the PDFs `generate_file` and `export_tabular_data` produce. A tabular export keeps
what the tabular preview shows: number formats, the header colour, the total row, highlighted cells and
charts, with each sheet on its own page and wide sheets in landscape.

#### Options

```csharp
builder.Services.Configure<PdfAgentOptions>(options =>
{
    options.Enabled = true;
    options.MaxDocumentBytes = 50L * 1024 * 1024;
    options.MaxWorkingDocuments = 40;
});

builder.Services.Configure<PdfCompositionOptions>(options =>
{
    options.DefaultPageSize = "Letter";
    options.DefaultMarginMm = 20;
    options.DefaultFontFamily = "Arial";
});

builder.Services.Configure<PdfPreviewOptions>(options =>
{
    options.MaxPages = 4;
    options.PageWidthPixels = 816;
});
```

#### Signing

`sign_pdf` signs with an identity the host configures, never with one supplied in the conversation.
Register it with `AddPdfSigning()`, which loads a PKCS#12 file; register your own
`IPdfSigningCertificateProvider` first to take the certificate from a key vault or a certificate store
instead:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddDocumentProcessing(documentProcessing => documentProcessing
            .AddPdf()
            .AddPdfSigning(options =>
            {
                options.CertificatePath = builder.Configuration["Pdf:CertificatePath"];
                options.CertificatePassword = builder.Configuration["Pdf:CertificatePassword"];
                options.TimestampAuthorityUrl = "https://timestamp.example.com";
            })
        )
    )
);
```

Without it, `sign_pdf` explains that signing is not configured; `verify_pdf_signature` works either way.

#### Good to know

- Every edit of a password-protected file saves an unprotected copy, and any edit makes existing digital
  signatures stop verifying. The agent says so, and protects or signs as the last step.
- Pages are numbered as they stand in the file, the way viewers and every PDF tool count them: a cover
  page shows no number but is counted, so the page after it reads "Page 2 of 3".
- Positions on a page are points from its top-left, as `search_pdf` reports them; on a page with a
  `/Rotate` entry they are in the page's unrotated frame.
- For the Orchard Core integration, see [CrestApps for Orchard Core](https://orchardcore.crestapps.com).

### Images

When your deployment supports vision, users can upload supported image files alongside standard documents. This enables image-aware chat scenarios such as describing screenshots, extracting visible text, or answering questions about diagrams and photos.

## Download Links and Citations

Uploaded documents and generated deliverables can both appear as downloadable references in chat.

Use these registrations together when you want document references to render as clickable downloads:

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

Generated downloads are kept separate from user-uploaded source documents, which helps hosts clean up conversation artifacts without touching knowledge uploads.

## File Types

Out of the box, the document features support common text, document, image, and tabular formats. Add the packages you need:

- `AddOpenXml()` for Office formats such as Word, PowerPoint, and Excel
- `AddPdf()` for PDF reading, the PDF writer and the PDF Agent (`AddPdfSigning()` adds signing)
- `AddMarkdown()` for Markdown-aware normalization and chunking

Use the document upload options to control which extensions your app accepts.

## Common Setup Choices

### Entity Framework Core stores

```csharp
.AddDocumentProcessing(documentProcessing => documentProcessing
    .AddEntityCoreStores()
    .AddOpenXml()
    .AddPdf()
    .AddReferenceDownloads()
)
```

### YesSql stores

```csharp
.AddDocumentProcessing(documentProcessing => documentProcessing
    .AddYesSqlStores()
    .AddOpenXml()
    .AddPdf()
    .AddReferenceDownloads()
)
```

Pick the store stack that matches the rest of your app.

## Upload Configuration

Use `ChatDocumentsOptions` to decide which file types users can attach.

```csharp
services.Configure<ChatDocumentsOptions>(options =>
{
    options.Add(".rtf", embeddable: true);
    options.Add(".tsv", embeddable: false);
});
```

Use the configured option values in both your UI and server-side validation so the visible upload guidance matches what the app actually supports.

## Storage

Uploaded files are stored through `IDocumentFileStore`. The default setup uses local storage, but you can replace it when you want a different backend such as cloud blob storage.

```csharp
builder.Services.AddSingleton<IDocumentFileStore, AzureBlobDocumentFileStore>();
```

## Extending the Experience

If you need another file format, register a custom reader for that extension and keep the rest of the document pipeline the same.

```csharp
builder.Services.AddCoreAIIngestionDocumentReader<MyCustomReader>(".custom", ".myformat");
```

## When to Use AI Documents

Choose AI Documents when your app needs any of the following:

- chat over uploaded knowledge files
- searchable document context with citations
- spreadsheet and CSV workflows in chat
- downloadable generated files tied to a conversation
- multimodal chat that includes image uploads
