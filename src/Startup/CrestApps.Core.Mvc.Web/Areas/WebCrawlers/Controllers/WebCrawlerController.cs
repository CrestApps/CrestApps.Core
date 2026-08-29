using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;
using CrestApps.Core.Mvc.Web.Areas.WebCrawlers.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Mvc.Web.Areas.WebCrawlers.Controllers;

[Area("WebCrawlers")]
[Authorize(Policy = "Admin")]
public sealed class WebCrawlerController : Controller
{
    private readonly ISourceCatalogManager<WebCrawler> _manager;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly IReadOnlyList<WebCrawlerStrategyDescriptor> _strategies;

    public WebCrawlerController(
        ISourceCatalogManager<WebCrawler> manager,
        IAIDataSourceStore dataSourceStore,
        IAIDataSourceIndexingQueue indexingQueue,
        IOptions<WebCrawlerStrategyOptions> strategyOptions)
    {
        _manager = manager;
        _dataSourceStore = dataSourceStore;
        _indexingQueue = indexingQueue;
        _strategies = strategyOptions.Value.Strategies
            .OrderBy(strategy => strategy.DisplayName.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IActionResult> Index()
    {
        var crawlers = await _manager.GetAllAsync();

        return View(crawlers);
    }

    public async Task<IActionResult> Create()
    {
        var model = new WebCrawlerViewModel();
        await PopulateDropdownsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WebCrawlerViewModel model)
    {
        var crawler = await _manager.NewAsync(model.Source, cancellationToken: HttpContext.RequestAborted);
        model.ApplyTo(crawler);

        await ValidateAsync(model, crawler);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        await _manager.CreateAsync(crawler);
        TempData["SuccessMessage"] = "Web crawler created successfully. Initial synchronization has been queued.";

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var crawler = await _manager.FindByIdAsync(id);

        if (crawler == null)
        {
            return NotFound();
        }

        var model = WebCrawlerViewModel.FromCrawler(crawler);
        await PopulateDropdownsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(WebCrawlerViewModel model)
    {
        var crawler = await _manager.FindByIdAsync(model.ItemId);

        if (crawler == null)
        {
            return NotFound();
        }

        model.ApplyTo(crawler);

        await ValidateAsync(model, crawler);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        await _manager.UpdateAsync(crawler);
        TempData["SuccessMessage"] = "Web crawler updated successfully. Synchronization has been queued.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var crawler = await _manager.FindByIdAsync(id);

        if (crawler != null)
        {
            await _manager.DeleteAsync(crawler);
            TempData["SuccessMessage"] = "Web crawler deleted successfully. Knowledge-base cleanup has been queued.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync(string id)
    {
        var crawler = await _manager.FindByIdAsync(id);

        if (crawler == null)
        {
            return NotFound();
        }

        var dataSource = string.IsNullOrWhiteSpace(crawler.AIDataSourceId)
            ? null
            : await _dataSourceStore.FindByIdAsync(crawler.AIDataSourceId);

        if (dataSource != null)
        {
            await _indexingQueue.QueueSyncDataSourceAsync(dataSource, HttpContext.RequestAborted);
            TempData["SuccessMessage"] = "Web crawler synchronization has been queued.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateAsync(WebCrawlerViewModel model, WebCrawler crawler)
    {
        var validation = await _manager.ValidateAsync(crawler);

        foreach (var error in validation.Errors)
        {
            var memberNames = error.MemberNames?.Any() == true ? error.MemberNames : [string.Empty];

            foreach (var memberName in memberNames)
            {
                ModelState.AddModelError(MapValidationMemberName(memberName), error.ErrorMessage);
            }
        }
    }

    private static string MapValidationMemberName(string memberName)
    {
        return memberName switch
        {
            nameof(WebCrawler.DisplayText) => nameof(WebCrawlerViewModel.DisplayText),
            nameof(WebCrawler.AIDataSourceId) => nameof(WebCrawlerViewModel.AIDataSourceId),
            nameof(WebCrawler.Source) => nameof(WebCrawlerViewModel.Source),
            nameof(SitemapWebCrawlerMetadata.BaseUrl) => nameof(WebCrawlerViewModel.SitemapBaseUrl),
            nameof(SitemapWebCrawlerMetadata.SitemapUrl) => nameof(WebCrawlerViewModel.SitemapUrl),
            _ => memberName,
        };
    }

    private async Task PopulateDropdownsAsync(WebCrawlerViewModel model)
    {
        var dataSources = await _dataSourceStore.GetAsync(AIDataSourceSourceTypes.Web);

        model.Strategies = _strategies
            .Select(strategy => new SelectListItem(strategy.DisplayName.Value, strategy.Strategy))
            .ToList();

        model.DataSources = dataSources
            .Select(dataSource => new SelectListItem(dataSource.DisplayText, dataSource.ItemId))
            .ToList();
    }
}
