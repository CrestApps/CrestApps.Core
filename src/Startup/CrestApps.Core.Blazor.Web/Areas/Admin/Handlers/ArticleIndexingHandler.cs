using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Startup.Shared.Models;
using CrestApps.Core.Startup.Shared.Services;
using CrestApps.Core.Support;

namespace CrestApps.Core.Blazor.Web.Areas.Admin.Handlers;

public sealed class ArticleIndexingHandler : CatalogEntryHandlerBase<Article>
{
    private readonly TimeProvider _timeProvider;
    private readonly ArticleIndexingService _indexingService;
    private readonly ILogger<ArticleIndexingHandler> _logger;

    public ArticleIndexingHandler(
        TimeProvider timeProvider,
        ArticleIndexingService indexingService,
        ILogger<ArticleIndexingHandler> logger)
    {
        _timeProvider = timeProvider;
        _indexingService = indexingService;
        _logger = logger;
    }

    public override Task InitializingAsync(InitializingContext<Article> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    public override Task UpdatingAsync(UpdatingContext<Article> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    public override Task InitializedAsync(InitializedContext<Article> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override Task CreatingAsync(CreatingContext<Article> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override Task ValidatingAsync(ValidatingContext<Article> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.Title))
        {
            context.Result.Fail(new ValidationResult("Title is required.", [nameof(Article.Title)]));
        }

        return Task.CompletedTask;
    }

    public override async Task CreatedAsync(CreatedContext<Article> context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _indexingService.IndexAsync(context.Model, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index article '{ArticleId}' after creation.", context.Model.ItemId);
        }
    }

    public override async Task UpdatedAsync(UpdatedContext<Article> context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _indexingService.IndexAsync(context.Model, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-index article '{ArticleId}' after update.", context.Model.ItemId);
        }
    }

    public override async Task DeletedAsync(DeletedContext<Article> context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _indexingService.DeleteAsync(context.Model.ItemId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove article '{ArticleId}' from search index after deletion.", context.Model.ItemId);
        }
    }

    private void EnsureCreatedDefaults(Article article)
    {
        if (article.CreatedUtc == default)
        {
            article.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }
    }

    private static Task PopulateAsync(Article article, JsonNode data)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        json.TryUpdateTrimmedStringValue(nameof(Article.Title), value => article.Title = value);
        json.TryUpdateTrimmedStringValue(nameof(Article.Description), value => article.Description = value);

        if (json.TryGetDateTimeValue(nameof(Article.CreatedUtc), out var createdUtc))
        {
            article.CreatedUtc = createdUtc;
        }

        return Task.CompletedTask;
    }
}
