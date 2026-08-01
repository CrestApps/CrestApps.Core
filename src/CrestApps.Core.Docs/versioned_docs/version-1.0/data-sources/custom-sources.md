---
sidebar_label: Custom Sources
sidebar_position: 5
title: Custom AI Data Source Sources
description: Register custom AI data source source handlers and keep external knowledge-base sources synchronized.
---

# Custom AI Data Source Sources

Use a custom source when your documents live outside a CrestApps-managed `SearchIndexProfile` and outside the built-in Elasticsearch, Azure AI Search, or PostgreSQL connectors.

## When to implement one

Create a custom `IAIDataSourceSourceHandler` when you need to read from:

- a custom search index schema
- a database or API that is not one of the built-in connectors
- an existing external system that already owns its own indexing lifecycle

## Registration pattern

Register three things:

1. the shared AI data source services
2. a source descriptor so the source type appears in UI selectors
3. a keyed `IAIDataSourceSourceHandler` for your source type

```csharp
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;

builder.Services
    .AddCoreAIServices()
    .AddCoreAIDataSourceRag()
    .Configure<AIDataSourceSourceOptions>(options => options.AddOrUpdate(
        "CustomCatalog",
        "Custom Catalog",
        "Read source documents from the custom catalog service."))
    .AddKeyedScoped<IAIDataSourceSourceHandler>("CustomCatalog", (sp, _) =>
        new CustomCatalogAIDataSourceSourceHandler(
            sp.GetRequiredService<ICustomCatalogClient>(),
            sp.GetRequiredService<ILogger<CustomCatalogAIDataSourceSourceHandler>>()));
```

## Handler contract

`IAIDataSourceSourceHandler` is responsible for the source side only:

- `ValidateAsync()` validates the source configuration stored on `AIDataSource`
- `GetReferenceTypeAsync()` returns the reference type written into knowledge-base chunks
- `ReadAsync()` returns every source document for a full rebuild
- `ReadByIdsAsync()` returns changed source documents for incremental sync

```csharp
public sealed class CustomCatalogAIDataSourceSourceHandler : IAIDataSourceSourceHandler
{
    public string SourceType => "CustomCatalog";

    public ValueTask ValidateAsync(
        AIDataSource dataSource,
        ValidationResultDetails result,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dataSource.ContentFieldName))
        {
            result.Fail(new ValidationResult(
                "Content field name is required.",
                [nameof(AIDataSource.ContentFieldName)]));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetReferenceTypeAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
        => ValueTask.FromResult("CustomCatalogItem");

    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(
        AIDataSource dataSource,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _client.GetAllAsync(cancellationToken))
        {
            yield return new KeyValuePair<string, SourceDocument>(
                item.Id,
                new SourceDocument(item.Title, item.Content));
        }
    }

    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(
        AIDataSource dataSource,
        IEnumerable<string> documentIds,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _client.GetByIdsAsync(documentIds, cancellationToken))
        {
            yield return new KeyValuePair<string, SourceDocument>(
                item.Id,
                new SourceDocument(item.Title, item.Content));
        }
    }
}
```

## Keeping the knowledge base synchronized

External and custom sources do **not** auto-sync themselves. Your integration must tell CrestApps.Core when source records change.

Inject `IAIDataSourceChangeNotifier` and call the method that matches the event:

```csharp
public sealed class CustomCatalogSyncBridge
{
    private readonly IAIDataSourceChangeNotifier _changeNotifier;

    public CustomCatalogSyncBridge(IAIDataSourceChangeNotifier changeNotifier)
    {
        _changeNotifier = changeNotifier;
    }

    public Task HandleCreatedOrUpdatedAsync(string dataSourceId, string recordId, CancellationToken cancellationToken)
        => _changeNotifier.QueueDocumentsAddedOrUpdatedAsync(dataSourceId, [recordId], cancellationToken).AsTask();

    public Task HandleDeletedAsync(string dataSourceId, string recordId, CancellationToken cancellationToken)
        => _changeNotifier.QueueDocumentsDeletedAsync(dataSourceId, [recordId], cancellationToken).AsTask();

    public Task HandleReseedAsync(string dataSourceId, CancellationToken cancellationToken)
        => _changeNotifier.QueueFullSyncAsync(dataSourceId, cancellationToken).AsTask();
}
```

Use these rules:

| Source event | Notification |
| --- | --- |
| one or more records added | `QueueDocumentsAddedOrUpdatedAsync()` |
| one or more records updated | `QueueDocumentsAddedOrUpdatedAsync()` |
| one or more records deleted | `QueueDocumentsDeletedAsync()` |
| re-seed, backfill, or uncertain drift | `QueueFullSyncAsync()` |

## Built-in secret handling pattern

If your source needs secrets, store them on `AIDataSource.Properties` metadata and protect them with `IDataProtectionProvider` using `AIDataSourceProtectionConstants.SourceSecretPurpose`.

That matches the built-in Elasticsearch, Azure AI Search, and PostgreSQL source handlers and keeps per-source credentials protected at rest.

## Safety net reconciliation

`AIDataSourceAlignmentBackgroundService` still runs nightly and performs a full reconciliation. Keep explicit notifications in place anyway so chat results stay fresh between scheduled passes.
