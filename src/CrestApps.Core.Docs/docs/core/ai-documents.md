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
            .AddWord()
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
- Download generated files such as exports and AI-authored documents
- Create, edit, design, preview and export PowerPoint decks through the Presentation Agent
- Create, edit, format, review, preview and export Word documents through the Word Agent
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

### Images

When your deployment supports vision, users can upload supported image files alongside standard documents. This enables image-aware chat scenarios such as describing screenshots, extracting visible text, or answering questions about diagrams and photos.

### Presentations and the Presentation Agent

`AddOpenXml()` registers PowerPoint support: the reader that indexes `.pptx` and `.potx` uploads for search, a
`.pptx` writer behind generated files, and the built-in **Presentation Agent** (`presentation-agent`). Like
the Tabular Data Agent it is a code-defined **system agent**: always available to the primary model, exposed
through the A2A host, and hidden from the agent pickers. The primary model delegates every request that
produces or works on a slide deck to it — "make me a 6-slide deck about our Q3 results", "use our company
template", "turn slide 4 into a timeline", "shorten every slide", "export it as PDF" — and every follow-up as
well. `generate_file` sends `.pptx` requests to the agent instead of writing a deck itself.

#### The workspace

Each chat session or chat interaction has a presentation workspace that persists between turns:

- **Uploaded decks are never changed.** An uploaded `.pptx` is copied into the workspace the first time a
  presentation tool runs, and every edit changes that working copy. A `.potx` upload is a template: the agent
  can build a new deck in its design, or restyle an existing deck with it.
- **Decks are real PowerPoint files.** Every change is applied as one all-or-nothing batch and saved as a new
  version, so the next turn, the preview and the export all see the same file. The last few versions are
  kept for `undo_presentation_change` and `compare_presentations`.
- The workspace is stored through `IDocumentFileStore` under the conversation's document folder, and is
  removed when the conversation is deleted or its history is cleared. Removing an upload removes the working
  copy made from it.

#### What the agent can do

| Area | Tools |
| --- | --- |
| Understanding | `get_presentation_outline`, `get_slide_content` (every element with its `#id`, role, position, text and style), `get_presentation_theme`, `search_presentation`, `extract_presentation_content` (text, notes, tables, charts, pictures, links, structure) |
| Slides | `create_presentation` (a theme preset, custom colours and fonts, or an uploaded template, with every slide in one call), `add_slide`, `duplicate_slide`, `delete_slide`, `move_slide`, `update_slide` (title, body, notes, layout, background, transition, visibility — many slides in one call) |
| Elements | `insert_slide_element` (text, bullets, 40 shapes, lines and connectors, tables, charts, pictures, icons, diagrams, video and audio links, groups), `update_slide_element` (move, resize, rotate, restyle, re-link, replace or crop a picture, alt text), `update_slide_text` (rewrite paragraphs in place, find and replace, speaker notes), `copy_slide_elements`, `delete_slide_element`, `group_slide_elements`, `arrange_slide_elements` (align, distribute, match sizes, layer, snap, auto-layout) |
| Data | `update_slide_table`, `update_slide_chart` (the chart's embedded workbook is updated too, so it stays editable in PowerPoint), `link_slide_data` and `refresh_slide_data`: tables and charts can take their rows from the conversation's uploaded spreadsheets with a read-only `tabular_sql` query, and stay linked to it |
| Design | `format_presentation` (consistent text, shape, table, chart and background styles, remembered as the deck's house style), `apply_slide_layout`, `apply_presentation_template`, `set_slide_background`, `update_presentation_theme`, `update_slide_master`, `apply_presentation_branding` (brand colours, fonts, logo, footer), `set_presentation_footer`, `update_presentation_sections`, `add_presentation_toc` (a linked agenda slide) |
| Visuals | `generate_slide_diagram` (process, chevron, cycle, timeline, hierarchy, pyramid, funnel, matrix, Venn and card diagrams built from editable shapes coloured from the theme), `generate_slide_image` (uses the image deployment) |
| Review | `analyze_presentation` (storyline, density, speaking time, visuals, design), `check_presentation` (overflowing or overlapping content, inconsistent titles and fonts, missing alt text and titles, low contrast, small text, dense slides, broken links, missing media, schema problems, content previews cannot draw), `compare_presentations` |
| Delivery | `preview_presentation`, `export_presentation` (`.pptx`, or PDF when `AddPdf()` is also registered), `export_presentation_content` (outline, speaker notes, presenter script with timings, handout or all text — inline or as `.md`, `.txt`, `.docx` or `.pdf`), `undo_presentation_change` |

The agent can also call `list_tabular_data` and `query_tabular_data` to choose the tables and queries a
slide's data comes from.

#### Previews

`preview_presentation` shows slides in the chat as pictures. Each slide is drawn as SVG from a display list
built by the same text layout the engine uses when it fits text into boxes, so a line that wraps in the
preview wraps at the same word in PowerPoint and in the exported PDF. The pictures are stored through
`IGeneratedDocumentService` and returned as `[fig:N]` markers, a preview is bounded and says which slides it
left out, and on a host that cannot serve pictures the slides are described in text instead. Content the
renderer cannot draw — SmartArt, 3D models, embedded objects, video frames — is shown as a labelled box, and
`check_presentation` says which slides have it. The preview format is resolvable but not requestable, so
`generate_file` cannot produce one.

#### Options

```csharp
builder.Services.Configure<PresentationWorkspaceOptions>(options =>
{
    options.MaxDecks = 10;         // decks kept per conversation
    options.MaxRevisions = 5;      // earlier versions kept for undo and comparison
    options.MaxSlides = 200;
    options.MaxImageBytes = 15 * 1024 * 1024;
});

builder.Services.Configure<PresentationPreviewOptions>(options =>
{
    options.MaxSlides = 8;         // slides per preview call
    options.Width = 960;
});

// Keep PowerPoint reading and writing but leave the agent out.
builder.Services.Configure<PresentationAgentOptions>(options => options.Enabled = false);
```

PDF export draws the same display lists into PDF pages. It embeds the fonts the host can resolve and falls
back to a common sans-serif typeface for the others, and gradients keep their first and last colours; the
`.pptx` file is unaffected by either.

### Word documents and the Word Agent

`AddWord()` registers the built-in **Word Agent** (`word-agent`). Like the Tabular Data Agent it is a
code-defined **system agent**: always available to the primary model, exposed through the A2A host, and hidden
from the agent pickers. The primary model delegates every request that produces or works on a Word document to
it — "write a two-page project proposal with a table of contents", "use our letterhead template", "make every
heading navy", "add page numbers", "turn tracking on and tighten the summary", "what changed between these two
versions?" — and every follow-up as well. A steering handler tells the primary model which uploads are Word
documents, so it delegates work on them instead of describing them. The `.docx` writer behind generated files
is part of `AddOpenXml()` and works without the agent.

#### The workspace

Each chat session or chat interaction has a Word workspace that persists between turns:

- **Uploaded documents are never changed.** An uploaded `.docx` is read in place; the first edit saves a
  working copy, and every later edit changes that copy. A `.docx` or `.dotx` upload can be the template a new
  document starts from, keeping its styles, page setup, headers and footers.
- **Documents are real Word files.** Every tool call applies its changes as one all-or-nothing edit and saves
  a new version, so the next turn, the preview and the export all see the same file. Elements are named by
  stable ids (Word's own paragraph ids), so a follow-up can change one paragraph without rebuilding the rest.
- **A copy never overwrites another document.** `save_as` with a name another working document already has
  saves under a numbered name, such as `final-2`, and the tool's answer says which.
- The workspace is stored through `IDocumentFileStore` under the conversation's document folder, and is
  removed, preview pictures included, when the conversation is deleted or its history is cleared. A file the
  store fails to delete is remembered and retried the next time the workspace is removed.
- Edits to one workspace are serialized by an in-process lock. On a multi-server deployment, route a
  conversation to one server, or two servers editing the same conversation at the same moment can lose one of
  the edits.
- Uploads are checked before they are opened: a `.docx` whose parts unpack past
  `WordAgentOptions.MaxUncompressedDocumentBytes`, that has more than 10,000 parts, or whose large parts are
  compressed far more than real documents are, is refused.

#### What the agent can do

| Area | Tools |
| --- | --- |
| Writing | `create_word_document` (a theme preset or custom fonts and colours, page setup, properties, an uploaded template, and the first content), `add_word_content` (headings, paragraphs with inline Markdown, Markdown, nested lists, quotes, code, tables, pictures, charts, captions, page breaks), `update_word_content`, `remove_word_content`, `move_word_content` |
| Structure | `add_word_section`, `add_word_page_break`, `add_word_toc`, `add_word_index`, `add_word_caption`, `add_word_cross_reference`, `add_word_bookmark`, `add_word_hyperlink`, `update_word_table` (cells, rows and columns, merging and splitting cells, cell fill and alignment, column widths, table style, banding and borders) |
| Design | `format_word_document` (the whole look, through the styles), `format_word_content` (chosen elements or a phrase in them), `manage_word_styles`, `set_word_page_layout` (size, margins, columns, page borders, page numbering), `set_word_page_background` (page colour, watermark), `add_word_header_footer` (text, page numbers and other fields, a logo) |
| Review | `manage_word_comments`, `manage_word_revisions` (track changes, accept, reject), `manage_word_protection` (editing restrictions with an optional password), `check_word_document` (accessibility, broken references and links, layout problems), `compare_word_documents` |
| Reading | `get_word_document`, `get_word_document_outline`, `search_word_document`, `extract_word_content` (Markdown, text, tables, links or pictures, optionally as a `.md`, `.txt` or `.csv` download) |
| Delivery | `preview_word`, `export_word` (the whole document, or chosen headings and elements as a separate file), `import_word` (a working copy of an upload, or a duplicate of a working document) |

The agent runs 32 hidden tools, and all of them stay in scope on every request: the orchestrator's relevance
scoping, which keeps the tools sharing the most words with a request once a profile has more than
`ScopingThreshold` tools, would otherwise hide the one a request needs, such as `format_word_document` for
"switch to the modern theme". Tables and charts can take their rows from the conversation's uploaded
spreadsheets with a read-only SQL query, and the agent can call `list_tabular_data` and `query_tabular_data`
to choose them. While change tracking is on, text and formatting edits from every tool are recorded as
revisions signed with `WordAgentOptions.Author`.

A document can be written in one call: a `toc` content block places a table of contents that is filled in
from the headings around it, list items nest under one another (bullets under a numbered item included), and
a table takes a format per column, from each column's `format`, a `formats` array, or a header that names the
unit, such as "Planned ($)". Follow-up edits keep to what they name:

- `update_word_content` replaces a phrase in chosen elements, in one heading's section (`under`), or in the
  whole document only when asked for `everywhere`.
- A list block added after an item of a list of the same kind continues that list, and a list written again
  with new items at its end adds only the new items.
- `update_word_table` names a row by the text of its first cell or its number (`get_word_document` lists both),
  writes a plain number the way the rest of its column is written, such as `$9,500.00`, and adds rows above
  the first one with `after_row: 0`.
- A cross-reference to a captioned table or figure refers to its caption, and `add_word_caption` on an element
  that has one changes its words instead of adding another.

Table columns are always the table's grid columns, counted from 1, so a row with merged cells is addressed
the same way as the others: a merged cell is found from any row and column it covers, and adding or removing
a column widens or narrows a merged cell instead of breaking its row.

`manage_word_protection` writes the editing restrictions Word enforces — read only, comments only, tracked
changes only, or filling in forms only — to the document settings. A password is hashed the way Word hashes
it (SHA-512 with a random salt and 100,000 rounds over Word's legacy password key), so Word asks for it to
stop the protection; the password itself is never stored or shown again, and Word only checks its first 15
characters. Protection is not encryption: the file still opens anywhere, and other applications may ignore
it.

`export_word` with `headings` or `ids` exports only that content as a separate `.docx`, leaving the working
document as it is. A heading brings everything under it up to the next heading of the same or a higher level;
the kept content keeps its sections' page layout, and comments and bookmarks whose content was left out are
removed.

Tracked changes are listed, accepted and rejected wherever Word keeps them — the body, headers, footers,
footnotes, endnotes and comments — including inserted and deleted table rows and cells, moved text, and
changes to paragraph, table, cell and page layout. The few kinds only Word can apply, such as numbering and
equation changes, are listed as such, and accepting or rejecting all changes stops with a message while any
of them remain.

When a document is refreshed before a preview or an export, a table of contents is rebuilt only when it lists
headings; a table of figures and other field results Word wrote are kept as Word left them. The exported file
asks Word to update its fields on opening only when something could not be refreshed here, such as a page
number past the layout limit.

#### Previews

`preview_word` shows pages in the chat as pictures. An in-process layout engine lays the document out into
pages — styles, lists, tables, pictures, charts, headers and footers, sections and columns — and draws each
page as SVG; the same layout gives the table of contents and cross-references their page numbers. Text is
measured with standard font metrics, so line and page breaks are close to Word's but not exact for every
typeface. The pictures are stored through `IGeneratedDocumentService` and returned as `[fig:N]` markers;
every colour taken from a document is validated before it is drawn, and links only keep web, mail and
in-document targets. On a host that cannot serve pictures the pages are described in text instead.

The layout is bounded so an uploaded file cannot exhaust the host: it stops at `MaxLayoutPages`, tables and
content controls nested more than 32 deep are drawn as placeholders, tables keep at most 63 columns and
sections at most 45, a paragraph is laid out up to its first 250,000 characters, and a picture larger than
`MaxImageBytesPerPage` is drawn as a labelled placeholder rather than read.

#### Options

```csharp
builder.Services.Configure<WordAgentOptions>(options =>
{
    options.MaxWorkingDocuments = 40;                // documents kept per conversation, duplicates included
    options.MaxDocumentBytes = 50L * 1024 * 1024;    // the largest document opened or kept
    options.MaxUncompressedDocumentBytes = 256L * 1024 * 1024; // the most a document may unpack to
    options.MaxImageBytes = 10 * 1024 * 1024;        // the largest picture a document places
    options.MaxToolResponseCharacters = 24_000;      // longer tool answers are cut, saying how to ask for the rest
    options.Author = "AI Assistant";                 // the name comments and tracked changes are signed with
});

builder.Services.Configure<WordPreviewOptions>(options =>
{
    options.MaxPages = 4;                            // pages shown when the agent asks for no particular pages
    options.MaxRenderPages = 12;                     // the most pages one call draws when it names them
    options.PageWidthPixels = 816;                   // the width a page is drawn at: a letter page at 96 dpi
    options.MaxImageBytesPerPage = 3 * 1024 * 1024;  // pictures past it are drawn as labelled placeholders
    options.MaxLayoutPages = 500;                    // pages laid out per document; page counts past it are a lower bound
});

// Keep the .docx writer but leave the agent out.
builder.Services.Configure<WordAgentOptions>(options => options.Enabled = false);
```

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

- `AddOpenXml()` for Office formats such as Word, PowerPoint (`.pptx` and `.potx` templates), and Excel, and for the Presentation Agent
- `AddPdf()` for PDF reading
- `AddWord()` for the Word Agent
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
