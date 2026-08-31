using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.DataSources;
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
/// required fields plus the selected strategy's settings, and keeps the target data source's knowledge
/// base aligned by queueing a full synchronization when a crawler changes.
/// </summary>
internal sealed class WebCrawlerCatalogHandler : CatalogEntryHandlerBase<WebCrawler>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly IWebCrawlStateStore _crawlStateStore;
    private readonly IWebCrawlerStrategyResolver _strategyResolver;
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
    /// <param name="logger">The logger.</param>
    public WebCrawlerCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IAIDataSourceStore dataSourceStore,
        IAIDataSourceIndexingQueue indexingQueue,
        IWebCrawlStateStore crawlStateStore,
        IWebCrawlerStrategyResolver strategyResolver,
        ILogger<WebCrawlerCatalogHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _dataSourceStore = dataSourceStore;
        _indexingQueue = indexingQueue;
        _crawlStateStore = crawlStateStore;
        _strategyResolver = strategyResolver;
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
            context.Result.Fail(new ValidationResult("A target Web data source is required.", [nameof(WebCrawler.AIDataSourceId)]));
        }

        if (string.IsNullOrWhiteSpace(crawler.Source))
        {
            context.Result.Fail(new ValidationResult("A crawl strategy is required.", [nameof(WebCrawler.Source)]));

            return;
        }

        var strategy = _strategyResolver.Get(crawler.Source);

        if (strategy is null)
        {
            context.Result.Fail(new ValidationResult("The selected crawl strategy is not supported.", [nameof(WebCrawler.Source)]));

            return;
        }

        await strategy.ValidateAsync(crawler, context.Result, cancellationToken);
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
