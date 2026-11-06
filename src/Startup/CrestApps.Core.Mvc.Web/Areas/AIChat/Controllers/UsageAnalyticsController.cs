using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Mvc.Web.Areas.AIChat.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.Controllers;

[Area("AIChat")]
[Authorize(Policy = "Admin")]
public sealed class UsageAnalyticsController : Controller
{
    private readonly IAICompletionUsageService _usageService;
    private readonly IAIDeploymentStore _deploymentStore;

    public UsageAnalyticsController(
        IAICompletionUsageService usageService,
        IAIDeploymentStore deploymentStore)
    {
        _usageService = usageService;
        _deploymentStore = deploymentStore;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new UsageAnalyticsIndexViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName(nameof(Index))]
    public async Task<IActionResult> IndexPost(UsageAnalyticsIndexViewModel model)
    {
        var records = await _usageService.GetAsync(model.StartDateUtc, model.EndDateUtc);
        var pricing = await _deploymentStore.GetPricingByDeploymentAsync();
        ApplyReport(model, records, pricing);

        return View("Index", model);
    }

    private static void ApplyReport(UsageAnalyticsIndexViewModel model, IReadOnlyList<AICompletionUsageRecord> records, IReadOnlyDictionary<string, AIDeploymentPricing> pricing)
    {
        model.ShowReport = true;
        var relevantRecords = records.Where(record => !string.IsNullOrEmpty(record.SessionId) || !string.IsNullOrEmpty(record.InteractionId)).ToList();
        model.TotalCalls = relevantRecords.Count;
        model.TotalSessions = relevantRecords.Select(record => record.SessionId).Where(sessionId => !string.IsNullOrEmpty(sessionId)).Distinct(StringComparer.Ordinal).Count();
        model.TotalChatInteractions = relevantRecords.Select(record => record.InteractionId).Where(interactionId => !string.IsNullOrEmpty(interactionId)).Distinct(StringComparer.Ordinal).Count();
        model.TotalTokens = relevantRecords.Sum(record => (long)record.TotalTokenCount);
        model.Rows = relevantRecords.GroupBy(record => new
        {
            UserLabel = GetUserLabel(record),
            record.IsAuthenticated,
            ClientName = record.ClientName ?? "Unknown",
            ModelName = record.ModelName ?? record.DeploymentName ?? "Unknown",
            AIUsageCostEstimator.GetPricing(pricing, record)?.Currency,
        }).Select(group =>
        {
            var latencySamples = group.Where(record => record.ResponseLatencyMs > 0).ToList();

            return new AICompletionUsageSummaryViewModel
            {
                UserLabel = group.Key.UserLabel,
                IsAuthenticated = group.Key.IsAuthenticated,
                ClientName = group.Key.ClientName,
                ModelName = group.Key.ModelName,
                TotalCalls = group.Count(),
                TotalSessions = group.Select(record => record.SessionId).Where(sessionId => !string.IsNullOrEmpty(sessionId)).Distinct(StringComparer.Ordinal).Count(),
                TotalChatInteractions = group.Select(record => record.InteractionId).Where(interactionId => !string.IsNullOrEmpty(interactionId)).Distinct(StringComparer.Ordinal).Count(),
                TotalInputTokens = group.Sum(record => (long)record.InputTokenCount),
                TotalOutputTokens = group.Sum(record => (long)record.OutputTokenCount),
                TotalCachedInputTokens = group.Sum(record => (long)record.CachedInputTokenCount),
                OtherUsage = UsageCostFormatter.DescribeOtherUsage(group.ToList()),
                TotalTokens = group.Sum(record => (long)record.TotalTokenCount),
                AverageResponseLatencyMs = latencySamples.Count > 0 ? Math.Round(latencySamples.Average(record => record.ResponseLatencyMs), 0) : 0,
                Currency = group.Key.Currency,
                EstimatedCost = AIUsageCostEstimator.Estimate(group, pricing),
            };
        }).OrderByDescending(row => row.TotalTokens)
        .ThenByDescending(row => row.TotalCalls)
        .ThenBy(row => row.UserLabel, StringComparer.OrdinalIgnoreCase)
        .ToList();
        model.EstimatedCosts = UsageCostFormatter.Totals(model.Rows.Select(row => (row.Currency, row.EstimatedCost)));
    }

    private static string GetUserLabel(AICompletionUsageRecord record)
    {
        if (!string.IsNullOrEmpty(record.UserName))
        {
            return record.UserName;
        }

        if (record.IsAuthenticated && !string.IsNullOrEmpty(record.UserId))
        {
            return record.UserId;
        }

        return "Anonymous";
    }
}
