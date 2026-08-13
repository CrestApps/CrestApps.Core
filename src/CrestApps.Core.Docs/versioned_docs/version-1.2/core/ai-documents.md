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
- Download generated files such as exports and AI-authored documents
- Include supported images in chat flows

## Supported Experiences

### Text and knowledge files

For text-heavy files such as Markdown, text, Word, PDF, HTML, JSON, or XML, CrestApps.Core extracts the useful content and makes it available during conversation. This is the main path for summaries, Q&A, reviews, rewrites, extraction, and similar knowledge tasks.

### Tabular files

CSV and Excel uploads are handled as structured data instead of plain text. That means the AI can filter, update, reshape, and export rows without asking the model to copy large tables into the prompt.

This is the recommended path for tasks such as:

- filling blank cells
- filtering rows
- adding calculated columns
- exporting an updated spreadsheet for download

Under the hood, tabular workflows are handled by the built-in **Tabular Data Agent**. It is a code-defined, always-available **system agent** that stays hidden from the AI Profile and Chat Interaction agent pickers, yet still participates in orchestration and is exposed through the A2A host for remote clients.

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
- `AddPdf()` for PDF reading
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
