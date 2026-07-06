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
    .AddDownloadAIDocumentEndpoint();
```

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

## Custom File Formats

If you need to support a format that is not built in, register a custom reader for the new extension.

```csharp
builder.Services.AddCoreAIIngestionDocumentReader<MyCustomReader>(".custom", ".myformat");
```

This lets you extend document support without changing the rest of the document-processing setup.
