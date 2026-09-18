using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.WebCrawlers;
using CrestApps.Core.AI.WebCrawlers.Strategies;
using CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;
using CrestApps.Core.Mvc.Web.Areas.WebCrawlers.Services;
using CrestApps.Core.Mvc.Web.Areas.WebCrawlers.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
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
    private readonly IWebCrawlerReindexPlanner _reindexPlanner;
    private readonly IWebCrawlStateStore _stateStore;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IReadOnlyList<WebCrawlerStrategyDescriptor> _strategies;

    public WebCrawlerController(
        ISourceCatalogManager<WebCrawler> manager,
        IAIDataSourceStore dataSourceStore,
        IWebCrawlerReindexPlanner reindexPlanner,
        IWebCrawlStateStore stateStore,
        IAIDeploymentManager deploymentManager,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<WebCrawlerStrategyOptions> strategyOptions)
    {
        _manager = manager;
        _dataSourceStore = dataSourceStore;
        _reindexPlanner = reindexPlanner;
        _stateStore = stateStore;
        _deploymentManager = deploymentManager;
        _dataProtectionProvider = dataProtectionProvider;
        _strategies = strategyOptions.Value.Strategies
            .OrderBy(strategy => strategy.DisplayName.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IActionResult> Index()
    {
        var crawlers = await _manager.GetAllAsync();

        // Only the strategy-backed records belong here. The connector-backed ones read files and are
        // managed on the File Sources screens, even though both kinds share one store.
        return View(WebCrawlerSourceFilter.SelectCrawlStrategyRecords(crawlers, _strategies));
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
        if (!WebCrawlerSourceFilter.IsCrawlStrategy(model.Source, _strategies))
        {
            ModelState.AddModelError(nameof(WebCrawlerViewModel.Source), "Select a crawl strategy. A folder or file server is set up under File Sources.");
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        var crawler = await _manager.NewAsync(model.Source, cancellationToken: HttpContext.RequestAborted);
        model.ApplyTo(crawler, _dataProtectionProvider);

        await ValidateAsync(crawler);

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

        if (crawler == null || !IsCrawlStrategyRecord(crawler))
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

        // A connector-backed record saved through this form would lose its connector settings, since this
        // form cannot offer its source. Such a record is only reachable by an old link, and is refused.
        if (crawler == null || !IsCrawlStrategyRecord(crawler))
        {
            return NotFound();
        }

        model.ApplyTo(crawler, _dataProtectionProvider);

        await ValidateAsync(crawler);

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

        if (crawler == null || !IsCrawlStrategyRecord(crawler))
        {
            return NotFound();
        }

        // Re-crawl just this site: discover its pages and queue the new/changed ones for indexing into the
        // target data source, without rebuilding the other crawlers mapped to that data source.
        var result = await _reindexPlanner.PlanAndEnqueueAsync(crawler, HttpContext.RequestAborted);

        if (result.Status is WebCrawlerReindexStatus.DiscoveryFailed or WebCrawlerReindexStatus.NoPagesDiscovered)
        {
            TempData["ErrorMessage"] = result.Message;
        }
        else
        {
            TempData["SuccessMessage"] = $"Web crawler synchronization queued: {result.NewCount} new, {result.ChangedCount} changed, {result.RemovedCount} removed.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetState(string id)
    {
        var crawler = await _manager.FindByIdAsync(id);

        if (crawler == null)
        {
            return NotFound();
        }

        // Forgetting what was read, not what was stored. The next run sees every item as new and re-reads
        // it, which is how an operator recovers from a bad run without deleting the data source.
        await _stateStore.DeleteByCrawlerIdAsync(crawler.ItemId, HttpContext.RequestAborted);

        TempData["SuccessMessage"] = "Indexer state cleared. The next run will re-read every item.";

        return RedirectToAction(nameof(Index));
    }

    private bool IsCrawlStrategyRecord(WebCrawler crawler)
        => WebCrawlerSourceFilter.IsCrawlStrategy(crawler.Source, _strategies);

    private async Task ValidateAsync(WebCrawler crawler)
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
        var webDataSources = await _dataSourceStore.GetAsync(AIDataSourceSourceTypes.Web);

        model.Strategies = _strategies
            .Select(strategy => new SelectListItem(strategy.DisplayName.Value, strategy.Strategy))
            .ToList();

        // A crawl strategy only ever feeds a Web data source. The File ones are offered on the File Sources
        // screens instead.
        model.DataSources = webDataSources
            .Select(dataSource => new SelectListItem(dataSource.DisplayText, dataSource.ItemId))
            .ToList();

        model.VisionDeployments = await BuildDeploymentsAsync(AIDeploymentSlotNames.Vision);
        model.UtilityDeployments = await BuildDeploymentsAsync(AIDeploymentSlotNames.Utility);
    }

    /// <summary>
    /// Lists the deployments that qualify for a slot, with an empty entry meaning "use the host's".
    /// </summary>
    /// <param name="slotName">The slot.</param>
    /// <returns>The list items.</returns>
    private async Task<List<SelectListItem>> BuildDeploymentsAsync(string slotName)
    {
        var deployments = await _deploymentManager.GetAllBySlotAsync(slotName);

        var items = new List<SelectListItem>
        {
            new("Use the application default", string.Empty),
        };

        items.AddRange(deployments.Select(deployment => new SelectListItem(deployment.Name, deployment.Name)));

        return items;
    }
}
