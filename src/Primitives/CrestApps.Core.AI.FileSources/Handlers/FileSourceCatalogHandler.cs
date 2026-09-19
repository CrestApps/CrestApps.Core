using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.FileSources.Handlers;

/// <summary>
/// Authoritative catalog handler for <see cref="FileSource"/>: applies create-time defaults, validates
/// required fields plus the selected connector's settings, forgets what a deleted source had read, and keeps
/// the target data source's knowledge base aligned by queueing a full synchronization when a source changes.
/// </summary>
/// <remarks>
/// A file source's <see cref="SourceCatalogEntry.Source"/> names an ingestion
/// connector, and a connector produces typed knowledge objects that only an ingested data source reads. That
/// is the whole of the difference from <c>WebCrawlerCatalogHandler</c>, which validates a crawl strategy and
/// allows the <c>Web</c> data sources a strategy can also fill.
/// </remarks>
internal sealed class FileSourceCatalogHandler : CatalogEntryHandlerBase<FileSource>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly IIngestionItemStateStore _stateStore;
    private readonly IIngestionConnectorResolver _connectorResolver;
    private readonly ILogger<FileSourceCatalogHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSourceCatalogHandler"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="dataSourceStore">The AI data source store.</param>
    /// <param name="indexingQueue">The data source indexing queue.</param>
    /// <param name="stateStore">The per-item state store.</param>
    /// <param name="connectorResolver">The ingestion connector resolver.</param>
    /// <param name="logger">The logger.</param>
    public FileSourceCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IAIDataSourceStore dataSourceStore,
        IAIDataSourceIndexingQueue indexingQueue,
        IIngestionItemStateStore stateStore,
        IIngestionConnectorResolver connectorResolver,
        ILogger<FileSourceCatalogHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _dataSourceStore = dataSourceStore;
        _indexingQueue = indexingQueue;
        _stateStore = stateStore;
        _connectorResolver = connectorResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task InitializingAsync(InitializingContext<FileSource> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    /// <inheritdoc />
    public override async Task UpdatingAsync(UpdatingContext<FileSource> context, CancellationToken cancellationToken = default)
    {
        await PopulateAsync(context.Model, context.Data);
        context.Model.ModifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <inheritdoc />
    public override Task InitializedAsync(InitializedContext<FileSource> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task CreatingAsync(CreatingContext<FileSource> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task ValidatingAsync(ValidatingContext<FileSource> context, CancellationToken cancellationToken = default)
    {
        var fileSource = context.Model;

        if (string.IsNullOrWhiteSpace(fileSource.DisplayText))
        {
            context.Result.Fail(new ValidationResult("Display text is required.", [nameof(FileSource.DisplayText)]));
        }

        if (string.IsNullOrWhiteSpace(fileSource.AIDataSourceId))
        {
            context.Result.Fail(new ValidationResult("A target data source is required.", [nameof(FileSource.AIDataSourceId)]));
        }

        if (string.IsNullOrWhiteSpace(fileSource.Source))
        {
            context.Result.Fail(new ValidationResult("Select where the files are read from.", [nameof(FileSource.Source)]));

            return;
        }

        var connector = _connectorResolver.Get(fileSource.Source);

        if (connector is null)
        {
            context.Result.Fail(new ValidationResult("The selected connector is not supported.", [nameof(FileSource.Source)]));

            return;
        }

        await ValidateDataSourceAsync(fileSource, context.Result, cancellationToken);

        await connector.ValidateAsync(fileSource, context.Result, cancellationToken);
    }

    /// <summary>
    /// Checks that the file source feeds a data source of a kind its connector can actually fill.
    /// </summary>
    /// <param name="fileSource">The record being validated.</param>
    /// <param name="result">The validation result to add failures to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// A connector produces typed knowledge objects, and only an <c>Ingested</c> data source reads those.
    /// </remarks>
    private async Task ValidateDataSourceAsync(
        FileSource fileSource,
        ValidationResultDetails result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileSource.AIDataSourceId))
        {
            return;
        }

        var dataSource = await _dataSourceStore.FindByIdAsync(fileSource.AIDataSourceId, cancellationToken);

        if (dataSource is null)
        {
            result.Fail(new ValidationResult("The selected data source was not found.", [nameof(FileSource.AIDataSourceId)]));

            return;
        }

        if (string.Equals(dataSource.Source, AIDataSourceSourceTypes.File, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        result.Fail(new ValidationResult(
            "A file connector can only feed an Ingested data source.",
            [nameof(FileSource.AIDataSourceId)]));
    }

    /// <inheritdoc />
    public override Task CreatedAsync(CreatedContext<FileSource> context, CancellationToken cancellationToken = default)
        => QueueDataSourceSyncAsync(context.Model, nameof(CreatedAsync), cancellationToken);

    /// <inheritdoc />
    public override Task UpdatedAsync(UpdatedContext<FileSource> context, CancellationToken cancellationToken = default)
        => QueueDataSourceSyncAsync(context.Model, nameof(UpdatedAsync), cancellationToken);

    /// <inheritdoc />
    public override async Task DeletedAsync(DeletedContext<FileSource> context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _stateStore.DeleteBySourceIdAsync(context.Model.ItemId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete the item state of file source '{FileSourceId}'.", context.Model.ItemId);
        }

        await QueueDataSourceSyncAsync(context.Model, nameof(DeletedAsync), cancellationToken);
    }

    private async Task QueueDataSourceSyncAsync(FileSource fileSource, string eventName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileSource.AIDataSourceId))
        {
            return;
        }

        try
        {
            var dataSource = await _dataSourceStore.FindByIdAsync(fileSource.AIDataSourceId, cancellationToken);

            if (dataSource is null)
            {
                return;
            }

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("File source catalog event '{EventName}' queued a full synchronization for data source '{DataSourceId}'.", eventName, dataSource.ItemId);
            }

            await _indexingQueue.QueueSyncDataSourceAsync(dataSource, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue synchronization for file source '{FileSourceId}'.", fileSource.ItemId);
        }
    }

    private void EnsureCreatedDefaults(FileSource fileSource)
    {
        if (fileSource.CreatedUtc == default)
        {
            fileSource.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user is null)
        {
            return;
        }

        fileSource.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        fileSource.Author ??= user.Identity?.Name;
    }

    private static Task PopulateAsync(FileSource fileSource, JsonNode data)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        json.TryUpdateTrimmedStringValue(nameof(FileSource.DisplayText), value => fileSource.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(FileSource.AIDataSourceId), value => fileSource.AIDataSourceId = value);
        json.TryUpdateTrimmedStringValue(nameof(FileSource.Source), value => fileSource.Source = value);
        json.TryUpdateTrimmedStringValue(nameof(FileSource.OwnerId), value => fileSource.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(FileSource.Author), value => fileSource.Author = value);

        if (json.TryGetBooleanValue(nameof(FileSource.Enabled), out var enabled))
        {
            fileSource.Enabled = enabled;
        }

        if (json.TryGetNullableInt32Value(nameof(FileSource.ReindexIntervalMinutes), out var reindexIntervalMinutes))
        {
            fileSource.ReindexIntervalMinutes = reindexIntervalMinutes;
        }

        return Task.CompletedTask;
    }
}
