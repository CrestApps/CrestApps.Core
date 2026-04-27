using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Mvc.Web.Areas.Indexing.ViewModels;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Mvc.Web.Areas.Indexing.Controllers;

[Area("Indexing")]
[Authorize(Policy = "Admin")]
public sealed class IndexProfileController : Controller
{
    private readonly ISearchIndexProfileManager _indexProfileManager;
    private readonly ISearchIndexProfileProvisioningService _provisioningService;
    private readonly IAIDeploymentStore _deploymentCatalog;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IndexProfileController> _logger;
    private readonly IReadOnlyList<IndexProfileSourceDescriptor> _sources;

    public IndexProfileController(
        ISearchIndexProfileManager indexProfileManager,
        ISearchIndexProfileProvisioningService provisioningService,
        IAIDeploymentStore deploymentCatalog,
        IServiceProvider serviceProvider,
        IOptions<IndexProfileSourceOptions> sourceOptions,
        ILogger<IndexProfileController> logger)
    {
        _indexProfileManager = indexProfileManager;
        _provisioningService = provisioningService;
        _deploymentCatalog = deploymentCatalog;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _sources = sourceOptions.Value.Sources
            .OrderBy(source => source.ProviderDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IActionResult> Index()
    {
        var profiles = await _indexProfileManager.GetAllAsync();

        return View(profiles);
    }

    public async Task<IActionResult> Create()
    {
        var model = new IndexProfileViewModel();
        await PopulateDropdownsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(IndexProfileViewModel model)
    {
        // Auto-generate Name from IndexName when not provided.

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            model.Name = model.IndexName?.Trim();
        }

        await ValidateAsync(model);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        var profile = new SearchIndexProfile();
        model.ApplyTo(profile);
        AddValidationErrors(await _provisioningService.CreateAsync(profile, HttpContext.RequestAborted));

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var profile = await _indexProfileManager.FindByIdAsync(id);

        if (profile == null)
        {
            return NotFound();
        }

        var model = IndexProfileViewModel.FromProfile(profile);
        await PopulateDropdownsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(IndexProfileViewModel model)
    {
        var profile = await _indexProfileManager.FindByIdAsync(model.ItemId);

        if (profile == null)
        {
            return NotFound();
        }

        await ValidateAsync(model, profile, profile.ItemId);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        profile.DisplayText = model.DisplayText;

        await _indexProfileManager.UpdateAsync(profile);
        await _indexProfileManager.SynchronizeAsync(profile, HttpContext.RequestAborted);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rebuild(string id)
    {
        var profile = await _indexProfileManager.FindByIdAsync(id);

        if (profile == null)
        {
            return NotFound();
        }

        var indexManager = ResolveIndexManager(profile);

        if (indexManager == null)
        {
            TempData["ErrorMessage"] = $"The search provider '{profile.ProviderName}' is not configured for remote index provisioning.";

            return RedirectToAction(nameof(Index));
        }

        try
        {
            profile.IndexFullName ??= indexManager.ComposeIndexFullName(profile);

            if (await indexManager.ExistsAsync(profile, HttpContext.RequestAborted))
            {
                await indexManager.DeleteAsync(profile, HttpContext.RequestAborted);
            }

            var fields = await _indexProfileManager.GetFieldsAsync(profile, HttpContext.RequestAborted);

            if (fields == null)
            {
                TempData["ErrorMessage"] = $"The index type '{profile.Type}' does not support rebuild.";

                return RedirectToAction(nameof(Index));
            }

            await indexManager.CreateAsync(profile, fields, HttpContext.RequestAborted);
            await _indexProfileManager.SynchronizeAsync(profile, HttpContext.RequestAborted);
            TempData["SuccessMessage"] = $"The index '{GetIndexDisplayName(profile)}' was rebuilt successfully.";
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                ex,
                "Unable to rebuild index profile '{IndexProfileId}' because the resolved remote index name is invalid.",
                profile.ItemId.SanitizeForLog());
            TempData["ErrorMessage"] = "Unable to rebuild the index because the remote index name is invalid.";
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to rebuild remote index '{IndexName}' for provider '{ProviderName}'.",
                GetIndexDisplayName(profile).SanitizeForLog(),
                profile.ProviderName.SanitizeForLog());
            TempData["ErrorMessage"] = $"Unable to rebuild the remote index '{GetIndexDisplayName(profile)}'.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reindex(string id)
    {
        var profile = await _indexProfileManager.FindByIdAsync(id);

        if (profile == null)
        {
            return NotFound();
        }

        try
        {
            await _indexProfileManager.ResetAsync(profile, HttpContext.RequestAborted);
            await _indexProfileManager.SynchronizeAsync(profile, HttpContext.RequestAborted);
            TempData["SuccessMessage"] = $"The index '{GetIndexDisplayName(profile)}' was reindexed successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to reindex remote index '{IndexName}' for provider '{ProviderName}'.",
                GetIndexDisplayName(profile).SanitizeForLog(),
                profile.ProviderName.SanitizeForLog());
            TempData["ErrorMessage"] = $"Unable to reindex the remote index '{GetIndexDisplayName(profile)}'.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var profile = await _indexProfileManager.FindByIdAsync(id);

        if (profile == null)
        {
            return NotFound();
        }

        var indexManager = _serviceProvider.GetKeyedService<ISearchIndexManager>(profile.ProviderName ?? string.Empty);

        if (indexManager == null)
        {
            _logger.LogWarning(
                "Skipping remote delete for index profile '{IndexProfileId}' because provider '{ProviderName}' is not registered.",
                profile.ItemId.SanitizeForLog(),
                profile.ProviderName.SanitizeForLog());

            await _indexProfileManager.DeleteAsync(profile);

            return RedirectToAction(nameof(Index));
        }

        profile.IndexFullName ??= indexManager.ComposeIndexFullName(profile);

        bool remoteIndexExists;
        try
        {
            remoteIndexExists = await indexManager.ExistsAsync(profile, HttpContext.RequestAborted);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                ex,
                "Skipping remote delete for index profile '{IndexProfileId}' because the resolved remote index name '{IndexName}' is invalid.",
                profile.ItemId.SanitizeForLog(),
                profile.IndexFullName.SanitizeForLog());
            remoteIndexExists = false;
        }

        if (remoteIndexExists)
        {
            try
            {
                await indexManager.DeleteAsync(profile, HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to delete remote index '{IndexName}' for provider '{ProviderName}'. The local index profile was not removed.",
                    profile.IndexFullName.SanitizeForLog(),
                    profile.ProviderName.SanitizeForLog());
                TempData["ErrorMessage"] = $"Unable to delete the remote index '{profile.IndexFullName}'. The index profile was not removed.";

                return RedirectToAction(nameof(Index));
            }
        }

        await _indexProfileManager.DeleteAsync(profile);

        return RedirectToAction(nameof(Index));
    }

    private ISearchIndexManager ResolveIndexManager(SearchIndexProfile profile)
    {
        try
        {
            return _serviceProvider.GetKeyedService<ISearchIndexManager>(profile.ProviderName ?? string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Skipping remote index operation for index profile '{IndexProfileId}' because provider '{ProviderName}' is not registered.",
                profile.ItemId.SanitizeForLog(),
                profile.ProviderName.SanitizeForLog());

            return null;
        }
    }

    private static string GetIndexDisplayName(SearchIndexProfile profile)
    {
        return profile.IndexFullName ?? profile.IndexName ?? profile.Name ?? profile.ItemId;
    }

    private async Task ValidateAsync(IndexProfileViewModel model, SearchIndexProfile profile = null, string excludeItemId = null)
    {
        if (!string.IsNullOrWhiteSpace(model.Name))
        {
            var existing = await _indexProfileManager.FindByNameAsync(model.Name.Trim());

            if (existing != null && existing.ItemId != excludeItemId)
            {
                ModelState.AddModelError(nameof(model.Name), "An index profile with this name already exists.");
            }
        }

        if (!string.IsNullOrWhiteSpace(model.ProviderName) &&
            !string.IsNullOrWhiteSpace(model.Type) &&
            !_sources.Any(source =>
                string.Equals(source.ProviderName, model.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(source.Type, model.Type, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(model.Type), "The selected provider does not support this index type.");
        }

        if (profile == null)
        {
            return;
        }

        AddValidationErrors(await _indexProfileManager.ValidateAsync(profile));
    }

    private void AddValidationErrors(ValidationResultDetails validationResult)
    {
        foreach (var error in validationResult.Errors)
        {
            var memberNames = error.MemberNames?.Any() == true
                ? error.MemberNames
                : [string.Empty];

            foreach (var memberName in memberNames)
            {
                ModelState.AddModelError(memberName, error.ErrorMessage);
            }
        }
    }

    private async Task PopulateDropdownsAsync(IndexProfileViewModel model)
    {
        model.Sources = _sources;
        model.Providers = _sources
            .GroupBy(source => source.ProviderName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(source => new SelectListItem(source.ProviderDisplayName, source.ProviderName))
            .ToList();
        model.Types = _sources
            .GroupBy(source => source.Type, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(source => new SelectListItem(source.DisplayName, source.Type))
            .ToList();

        var deployments = await _deploymentCatalog.GetAllAsync();

        model.EmbeddingDeployments = deployments
            .Where(d => d.Type == AIDeploymentType.Embedding)
                .Select(d => new SelectListItem(d.Name, d.ItemId));
    }

}
