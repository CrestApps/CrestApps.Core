using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Handlers;

internal sealed class AIDataSourceCatalogHandler : CatalogEntryHandlerBase<AIDataSource>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly ILogger<AIDataSourceCatalogHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDataSourceCatalogHandler"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="indexingQueue">The indexing queue.</param>
    /// <param name="logger">The logger.</param>
    public AIDataSourceCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IAIDataSourceIndexingQueue indexingQueue,
        ILogger<AIDataSourceCatalogHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _indexingQueue = indexingQueue;
        _logger = logger;
    }

    public override Task InitializingAsync(InitializingContext<AIDataSource> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    public override Task UpdatingAsync(UpdatingContext<AIDataSource> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    /// <summary>
    /// Initializeds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializedAsync(InitializedContext<AIDataSource> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task CreatingAsync(CreatingContext<AIDataSource> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task ValidatingAsync(ValidatingContext<AIDataSource> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.DisplayText))
        {
            context.Result.Fail(new ValidationResult("Display text is required.", [nameof(AIDataSource.DisplayText)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.SourceIndexProfileName))
        {
            context.Result.Fail(new ValidationResult("Source index profile is required.", [nameof(AIDataSource.SourceIndexProfileName)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.AIKnowledgeBaseIndexProfileName))
        {
            context.Result.Fail(new ValidationResult("AI knowledge base index profile is required.", [nameof(AIDataSource.AIKnowledgeBaseIndexProfileName)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.ContentFieldName))
        {
            context.Result.Fail(new ValidationResult("Content field name is required.", [nameof(AIDataSource.ContentFieldName)]));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Createds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task CreatedAsync(CreatedContext<AIDataSource> context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("AI data source catalog event '{EventName}' queued full synchronization for data source '{DataSourceId}'.", nameof(CreatedAsync), context.Model.ItemId);
            }

            await _indexingQueue.QueueSyncDataSourceAsync(context.Model, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue initial indexing for data source '{DataSourceId}'.", context.Model.ItemId);
        }
    }

    /// <summary>
    /// Updateds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task UpdatedAsync(UpdatedContext<AIDataSource> context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("AI data source catalog event '{EventName}' queued full synchronization for data source '{DataSourceId}'.", nameof(UpdatedAsync), context.Model.ItemId);
            }

            await _indexingQueue.QueueSyncDataSourceAsync(context.Model, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue synchronization for updated data source '{DataSourceId}'.", context.Model.ItemId);
        }
    }

    /// <summary>
    /// Deleteds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task DeletedAsync(DeletedContext<AIDataSource> context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("AI data source catalog event '{EventName}' queued cleanup for data source '{DataSourceId}'.", nameof(DeletedAsync), context.Model.ItemId);
            }

            await _indexingQueue.QueueDeleteDataSourceAsync(context.Model, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue cleanup for deleted data source '{DataSourceId}'.", context.Model.ItemId);
        }
    }

    private void EnsureCreatedDefaults(AIDataSource dataSource)
    {
        if (dataSource.CreatedUtc == default)
        {
            dataSource.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user == null)
        {
            return;
        }

        dataSource.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        dataSource.Author ??= user.Identity?.Name;
    }

    private static Task PopulateAsync(AIDataSource dataSource, JsonNode data)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        json.TryUpdateTrimmedStringValue(nameof(AIDataSource.DisplayText), value => dataSource.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(AIDataSource.OwnerId), value => dataSource.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(AIDataSource.Author), value => dataSource.Author = value);
        json.TryUpdateTrimmedStringValue(nameof(AIDataSource.SourceIndexProfileName), value => dataSource.SourceIndexProfileName = value);
        json.TryUpdateTrimmedStringValue(nameof(AIDataSource.AIKnowledgeBaseIndexProfileName), value => dataSource.AIKnowledgeBaseIndexProfileName = value);
        json.TryUpdateTrimmedStringValue(nameof(AIDataSource.KeyFieldName), value => dataSource.KeyFieldName = value);
        json.TryUpdateTrimmedStringValue(nameof(AIDataSource.TitleFieldName), value => dataSource.TitleFieldName = value);
        json.TryUpdateTrimmedStringValue(nameof(AIDataSource.ContentFieldName), value => dataSource.ContentFieldName = value);

        if (!json.TryUpdateTrimmedStringValue(nameof(AIDataSource.SourceIndexProfileName), value => dataSource.SourceIndexProfileName = value))
        {
            json.TryUpdateTrimmedStringValue("ProfileSource", value =>
            {
#pragma warning disable CS0618 // Type or member is obsolete
                dataSource.ProfileSource = value;
#pragma warning restore CS0618 // Type or member is obsolete
                dataSource.SourceIndexProfileName = value;
            });
        }

        json.TryUpdateTrimmedStringValue("ProfileSource", value =>
        {
#pragma warning disable CS0618 // Type or member is obsolete
            dataSource.ProfileSource = value;
#pragma warning restore CS0618 // Type or member is obsolete
        });

        json.TryUpdateTrimmedStringValue("Type", value =>
        {
#pragma warning disable CS0618 // Type or member is obsolete
            dataSource.Type = value;
#pragma warning restore CS0618 // Type or member is obsolete
        });

        if (json.TryGetDateTimeValue(nameof(AIDataSource.CreatedUtc), out var createdUtc))
        {
            dataSource.CreatedUtc = createdUtc;
        }

        MergeProperties(dataSource, json);

        return Task.CompletedTask;
    }

    private static void MergeProperties(AIDataSource dataSource, JsonObject json)
    {
        if (!json.TryGetPropertyValue(nameof(AIDataSource.Properties), out var propertiesNode))
        {
            return;
        }

        dataSource.Properties.Clear();

        if (propertiesNode is not JsonObject properties)
        {
            return;
        }

        foreach (var (key, value) in properties)
        {
            dataSource.Properties[key] = value.GetRawValue();
        }
    }
}
