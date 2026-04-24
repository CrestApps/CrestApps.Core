using CrestApps.Core.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Startup.Shared.Models;
using CrestApps.Core.Startup.Shared.Services;

namespace CrestApps.Core.Mvc.Web.Areas.Admin.Handlers;

public sealed class ArticleIndexingHandler : CatalogEntryHandlerBase<Article>
{
    private readonly ArticleIndexingService _indexingService;
    private readonly ILogger<ArticleIndexingHandler> _logger;

    public ArticleIndexingHandler(
        ArticleIndexingService indexingService,
        ILogger<ArticleIndexingHandler> logger)
    {
        _indexingService = indexingService;
        _logger = logger;
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
}
