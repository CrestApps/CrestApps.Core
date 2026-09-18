using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.WebCrawlers.Handlers;

/// <summary>
/// Authoritative catalog handler for <see cref="WebCrawler"/>: applies create-time defaults, validates
/// required fields plus the selected source's settings, and keeps the target data source's knowledge
/// base aligned by queueing a full synchronization when a crawler changes.
/// </summary>
/// <remarks>
/// A record's <see cref="WebCrawler.Source"/> names either a crawl strategy or an ingestion connector. A
/// crawl strategy may feed a <c>Web</c> data source (through the re-index planner) or an <c>Ingested</c>
/// one (through the indexer run service); a connector that is not also a strategy can only feed an
/// <c>Ingested</c> data source, because nothing else knows how to read the objects it produces.
/// </remarks>
internal sealed class WebCrawlerCatalogHandler : CatalogEntryHandlerBase<WebCrawler>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly IWebCrawlStateStore _crawlStateStore;
    private readonly IWebCrawlerStrategyResolver _strategyResolver;
    private readonly IIngestionConnectorResolver _connectorResolver;
    private readonly ILogger<WebCrawlerCatalogHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlerCatalogHandler"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="dataSourceStore">The AI data source store.</param>
    /// <param name="indexingQueue">The data source indexing queue.</param>
    /// <param name="crawlStateStore">The crawl-state store.</param>
    /// <param name="strategyResolver">The strategy resolver.</param>
    /// <param name="connectorResolver">The ingestion connector resolver.</param>
    /// <param name="logger">The logger.</param>
    public WebCrawlerCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IAIDataSourceStore dataSourceStore,
        IAIDataSourceIndexingQueue indexingQueue,
        IWebCrawlStateStore crawlStateStore,
        IWebCrawlerStrategyResolver strategyResolver,
        IIngestionConnectorResolver connectorResolver,
        ILogger<WebCrawlerCatalogHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _dataSourceStore = dataSourceStore;
        _indexingQueue = indexingQueue;
        _crawlStateStore = crawlStateStore;
        _strategyResolver = strategyResolver;
        _connectorResolver = connectorResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task InitializingAsync(InitializingContext<WebCrawler> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    /// <inheritdoc />
    public override async Task UpdatingAsync(UpdatingContext<WebCrawler> context, CancellationToken cancellationToken = default)
    {
        await PopulateAsync(context.Model, context.Data);
        context.Model.ModifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <inheritdoc />
    public override Task InitializedAsync(InitializedContext<WebCrawler> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task CreatingAsync(CreatingContext<WebCrawler> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task ValidatingAsync(ValidatingContext<WebCrawler> context, CancellationToken cancellationToken = default)
    {
        var crawler = context.Model;

        if (string.IsNullOrWhiteSpace(crawler.DisplayText))
        {
            context.Result.Fail(new ValidationResult("Display text is required.", [nameof(WebCrawler.DisplayText)]));
        }

        if (string.IsNullOrWhiteSpace(crawler.AIDataSourceId))
        {
            context.Result.Fail(new ValidationResult("A target data source is required.", [nameof(WebCrawler.AIDataSourceId)]));
        }

        if (string.IsNullOrWhiteSpace(crawler.Source))
        {
            context.Result.Fail(new ValidationResult("A crawl strategy or connector is required.", [nameof(WebCrawler.Source)]));

            return;
        }

        var strategy = _strategyResolver.Get(crawler.Source);
        var connector = strategy is null ? _connectorResolver.Get(crawler.Source) : null;

        if (strategy is null && connector is null)
        {
            context.Result.Fail(new ValidationResult("The selected crawl strategy or connector is not supported.", [nameof(WebCrawler.Source)]));

            return;
        }

        await ValidateDataSourceAsync(crawler, strategy is not null, context.Result, cancellationToken);

        if (strategy is not null)
        {
            await strategy.ValidateAsync(crawler, context.Result, cancellationToken);

            return;
        }

        await connector.ValidateAsync(crawler, context.Result, cancellationToken);
    }

    /// <summary>
    /// Checks that the record feeds a data source of a kind its source can actually fill.
    /// </summary>
    /// <param name="crawler">The record being validated.</param>
    /// <param name="isStrategy">Whether the source is a crawl strategy rather than a bare connector.</param>
    /// <param name="result">The validation result to add failures to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// A connector produces typed knowledge objects, and only an <c>Ingested</c> data source reads those. A
    /// crawl strategy can also feed a <c>Web</c> data source, which reads crawled pages directly.
    /// </remarks>
    private async Task ValidateDataSourceAsync(
        WebCrawler crawler,
        bool isStrategy,
        ValidationResultDetails result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(crawler.AIDataSourceId))
        {
            return;
        }

        var dataSource = await _dataSourceStore.FindByIdAsync(crawler.AIDataSourceId, cancellationToken);

        if (dataSource is null)
        {
            result.Fail(new ValidationResult("The selected data source was not found.", [nameof(WebCrawler.AIDataSourceId)]));

            return;
        }

        if (string.Equals(dataSource.Source, AIDataSourceSourceTypes.File, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (isStrategy && string.Equals(dataSource.Source, AIDataSourceSourceTypes.Web, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        result.Fail(new ValidationResult(
            isStrategy
                ? "A crawl strategy can only feed a Web or an Ingested data source."
                : "A file connector can only feed an Ingested data source.",
            [nameof(WebCrawler.AIDataSourceId)]));
    }

    /// <inheritdoc />
    public override Task CreatedAsync(CreatedContext<WebCrawler> context, CancellationToken cancellationToken = default)
        => QueueDataSourceSyncAsync(context.Model, nameof(CreatedAsync), cancellationToken);

    /// <inheritdoc />
    public override Task UpdatedAsync(UpdatedContext<WebCrawler> context, CancellationToken cancellationToken = default)
        => QueueDataSourceSyncAsync(context.Model, nameof(UpdatedAsync), cancellationToken);

    /// <inheritdoc />
    public override async Task DeletedAsync(DeletedContext<WebCrawler> context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _crawlStateStore.DeleteByCrawlerIdAsync(context.Model.ItemId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete crawl state for web crawler '{CrawlerId}'.", context.Model.ItemId);
        }

        await QueueDataSourceSyncAsync(context.Model, nameof(DeletedAsync), cancellationToken);
    }

    private async Task QueueDataSourceSyncAsync(WebCrawler crawler, string eventName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(crawler.AIDataSourceId))
        {
            return;
        }

        try
        {
            var dataSource = await _dataSourceStore.FindByIdAsync(crawler.AIDataSourceId, cancellationToken);

            if (dataSource is null)
            {
                return;
            }

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Web crawler catalog event '{EventName}' queued a full synchronization for data source '{DataSourceId}'.", eventName, dataSource.ItemId);
            }

            await _indexingQueue.QueueSyncDataSourceAsync(dataSource, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue synchronization for web crawler '{CrawlerId}'.", crawler.ItemId);
        }
    }

    private void EnsureCreatedDefaults(WebCrawler crawler)
    {
        if (crawler.CreatedUtc == default)
        {
            crawler.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user is null)
        {
            return;
        }

        crawler.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        crawler.Author ??= user.Identity?.Name;
    }

    private static Task PopulateAsync(WebCrawler crawler, JsonNode data)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        json.TryUpdateTrimmedStringValue(nameof(WebCrawler.DisplayText), value => crawler.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(WebCrawler.AIDataSourceId), value => crawler.AIDataSourceId = value);
        json.TryUpdateTrimmedStringValue(nameof(WebCrawler.Source), value => crawler.Source = value);
        json.TryUpdateTrimmedStringValue(nameof(WebCrawler.OwnerId), value => crawler.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(WebCrawler.Author), value => crawler.Author = value);

        if (json.TryGetBooleanValue(nameof(WebCrawler.Enabled), out var enabled))
        {
            crawler.Enabled = enabled;
        }

        if (json.TryGetNullableInt32Value(nameof(WebCrawler.ReindexIntervalMinutes), out var reindexIntervalMinutes))
        {
            crawler.ReindexIntervalMinutes = reindexIntervalMinutes;
        }

        return Task.CompletedTask;
    }
}
