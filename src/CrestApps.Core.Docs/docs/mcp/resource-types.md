---
sidebar_label: Resource Types
sidebar_position: 4
title: MCP Resource Types
description: Create custom MCP resource type handlers for FTP, SFTP, databases, or any protocol.
---

# MCP Resource Types

> Register custom resource type handlers to expose files, data, or content from any protocol as MCP resources.

## Quick Start

```csharp
builder.Services
    .AddCoreAIMcpServer()
    .AddCoreAIFtpMcpResources()
    .AddCoreAISftpMcpResources()
    .AddMcpResourceType<MyDatabaseResourceHandler>("database");
```

Or, using the builder pattern with stores:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddMcpServer(mcpServer => mcpServer
            .AddYesSqlStores()
            .AddFtpResources()
            .AddSftpResources()
        )
    )
);
```

## Problem & Solution

MCP resources represent files, URLs, or data that clients can read. FTP and SFTP handlers are available as optional packages (`CrestApps.Core.AI.Mcp.Ftp` and `CrestApps.Core.AI.Mcp.Sftp`), and applications often need additional resource types for databases, APIs, blob storage, or custom protocols. Resource type handlers provide a pluggable extension point.

## Built-in Resource Types

| Type | Handler | Protocol |
|------|---------|----------|
| `ftp` | `FtpResourceTypeHandler` | FTP/FTPS |
| `sftp` | `SftpResourceTypeHandler` | SFTP |
| `datasource-figure` | `DataSourceFigureResourceHandler` | Knowledge figures and charts in a `File` data source |

Register them explicitly with `AddCoreAIFtpMcpResources()`, `AddCoreAISftpMcpResources()` and
`AddCoreAIKnowledgeMcpResources()`.

### Knowledge figures

`CrestApps.Core.AI.Mcp.Knowledge` publishes one template,
`crestapps://datasource/{dataSourceId}/figure/{figureId}` — the same address a search result carries, so a
client can read a figure straight out of what a search told it. Listing resources never enumerates the
figures in a knowledge base; only the template is published.

A canonical identifier is short and guessable, so the handler establishes that the object belongs to the
data source in the address and is actually a figure or chart before it serves any bytes. Anything else is a
not-found.

## Registration

```csharp
builder.Services.AddMcpResourceType<MyHandler>("my-type", entry =>
{
    entry.DisplayName = "My Resource Type";
    entry.Description = "Reads resources from my custom source";
});
```

### `AddMcpResourceType<THandler>(type, configure?)`

| Parameter | Description |
|-----------|-------------|
| `type` | Unique type identifier string |
| `configure` | Optional action to set display name and description |

## Implementing a Resource Type Handler

```csharp
public sealed class BlobStorageResourceHandler : McpResourceTypeHandlerBase
{
    private readonly BlobServiceClient _blobClient;

    public BlobStorageResourceHandler(BlobServiceClient blobClient)
    {
        _blobClient = blobClient;
    }

    protected override async Task<McpResourceReadResult> GetResultAsync(
        McpResourceReadContext context,
        CancellationToken cancellationToken)
    {
        var containerClient = _blobClient.GetBlobContainerClient(context.ContainerName);
        var blobClient = containerClient.GetBlobClient(context.ResourcePath);

        var download = await blobClient.DownloadContentAsync(cancellationToken);
        var content = download.Value.Content.ToString();

        return new McpResourceReadResult
        {
            Contents = [new TextResourceContents
            {
                Text = content,
                Uri = context.Uri,
                MimeType = "text/plain",
            }],
        };
    }
}
```

## Key Interfaces

### `IMcpResourceTypeHandler`

```csharp
public interface IMcpResourceTypeHandler
{
    Task<McpResourceReadResult> ReadAsync(
        McpResourceReadContext context,
        CancellationToken cancellationToken = default);
}
```

### `McpResourceTypeHandlerBase`

A convenience base class — implement `GetResultAsync()` instead:

```csharp
protected abstract Task<McpResourceReadResult> GetResultAsync(
    McpResourceReadContext context,
    CancellationToken cancellationToken);
```
