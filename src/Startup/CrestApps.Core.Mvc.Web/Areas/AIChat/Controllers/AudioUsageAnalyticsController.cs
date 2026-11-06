using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Mvc.Web.Areas.AIChat.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.Controllers;

[Area("AIChat")]
[Authorize(Policy = "Admin")]
public sealed class AudioUsageAnalyticsController : Controller
{
    private readonly IAICompletionUsageService _usageService;
    private readonly IAIDeploymentStore _deploymentStore;

    public AudioUsageAnalyticsController(IAICompletionUsageService usageService, IAIDeploymentStore deploymentStore)
    {
        _usageService = usageService;
        _deploymentStore = deploymentStore;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new AudioUsageAnalyticsIndexViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName(nameof(Index))]
    public async Task<IActionResult> IndexPost(AudioUsageAnalyticsIndexViewModel model)
    {
        var records = await _usageService.GetAsync(model.StartDateUtc, model.EndDateUtc);

        model.ShowReport = true;
        model.Rows = AIAudioUsageReport.Build(records, model.Grouping, await _deploymentStore.GetPricingByDeploymentAsync());

        return View("Index", model);
    }
}
