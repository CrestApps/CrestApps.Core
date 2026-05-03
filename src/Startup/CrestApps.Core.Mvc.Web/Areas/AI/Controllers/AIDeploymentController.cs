using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Controllers;

[Area("AI")]
[Authorize(Policy = "Admin")]
public sealed class AIDeploymentController : Controller
{
    private readonly IAIDeploymentStore _deploymentStore;
    private readonly INamedSourceCatalog<AIDeployment> _deploymentCatalog;
    private readonly IAIProviderConnectionStore _connectionCatalog;

    private static readonly List<SelectListItem> _providers =
    [
        new("OpenAI", "OpenAI"),
        new("Azure OpenAI", "Azure"),
        new("Azure AI Inference (GitHub Models)", "AzureAIInference"),
        new("Azure AI Services", "AzureSpeech"),
        new("Ollama", "Ollama"),
    ];

    private static readonly List<SelectListItem> _authTypes =
    [
        new("API Key", "ApiKey"),
        new("Default Azure Credential", "Default"),
        new("Managed Identity", "ManagedIdentity"),
    ];

    public AIDeploymentController(
        IAIDeploymentStore deploymentStore,
        INamedSourceCatalog<AIDeployment> deploymentCatalog,
        IAIProviderConnectionStore connectionCatalog)
    {
        _deploymentStore = deploymentStore;
        _deploymentCatalog = deploymentCatalog;
        _connectionCatalog = connectionCatalog;
    }

    public async Task<IActionResult> Index()
    {
        var deployments = await _deploymentStore.GetAllAsync();

        return View(deployments
                    .Select(AIDeploymentViewModel.FromDeployment)
                    .OrderBy(static deployment => deployment.TechnicalName, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    public async Task<IActionResult> Create()
    {
        var model = new AIDeploymentViewModel();
        await PopulateDropdownsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AIDeploymentViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.TechnicalName))
        {
            ModelState.AddModelError(nameof(model.TechnicalName), "Technical name is required.");
        }

        if (string.IsNullOrWhiteSpace(model.ModelName))
        {
            ModelState.AddModelError(nameof(model.ModelName), "Model name is required.");
        }

        if (string.IsNullOrWhiteSpace(model.ClientName))
        {
            ModelState.AddModelError(nameof(model.ClientName), "Provider is required.");
        }

        if (!model.GetDeploymentType().IsValidSelection())
        {
            ModelState.AddModelError(nameof(model.SelectedTypes), "At least one deployment type is required.");
        }

        if (!model.UsesStandaloneProvider() && string.IsNullOrWhiteSpace(model.ConnectionName))
        {
            ModelState.AddModelError(nameof(model.ConnectionName), "Connection is required when the selected provider uses a shared connection.");
        }

        await ValidateUniqueNameAsync(model.TechnicalName);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        // Clear connection for standalone providers.

        if (model.UsesStandaloneProvider())
        {
            model.ConnectionName = null;
        }

        var deployment = new AIDeployment
        {
            ItemId = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
        };

        model.ApplyTo(deployment);

        await _deploymentCatalog.CreateAsync(deployment);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var deployment = await _deploymentCatalog.FindByIdAsync(id);

        if (deployment == null)
        {
            return NotFound();
        }

        if (deployment.IsReadOnly)
        {
            TempData["ErrorMessage"] = "Deployments defined in appsettings are read-only and cannot be edited from the UI.";

            return RedirectToAction(nameof(Index));
        }

        var model = AIDeploymentViewModel.FromDeployment(deployment);
        await PopulateDropdownsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AIDeploymentViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.ModelName))
        {
            ModelState.AddModelError(nameof(model.ModelName), "Model name is required.");
        }

        if (string.IsNullOrWhiteSpace(model.TechnicalName))
        {
            ModelState.AddModelError(nameof(model.TechnicalName), "Technical name is required.");
        }

        if (string.IsNullOrWhiteSpace(model.ClientName))
        {
            ModelState.AddModelError(nameof(model.ClientName), "Provider is required.");
        }

        if (!model.GetDeploymentType().IsValidSelection())
        {
            ModelState.AddModelError(nameof(model.SelectedTypes), "At least one deployment type is required.");
        }

        if (!model.UsesStandaloneProvider() && string.IsNullOrWhiteSpace(model.ConnectionName))
        {
            ModelState.AddModelError(nameof(model.ConnectionName), "Connection is required when the selected provider uses a shared connection.");
        }

        await ValidateUniqueNameAsync(model.TechnicalName, model.ItemId);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        var existing = await _deploymentCatalog.FindByIdAsync(model.ItemId);

        if (existing == null)
        {
            return NotFound();
        }

        if (existing.IsReadOnly)
        {
            TempData["ErrorMessage"] = "Deployments defined in appsettings are read-only and cannot be edited from the UI.";

            return RedirectToAction(nameof(Index));
        }

        // Clear connection for standalone providers.

        if (model.UsesStandaloneProvider())
        {
            model.ConnectionName = null;
        }

        model.ApplyTo(existing);

        await _deploymentCatalog.UpdateAsync(existing);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var deployment = await _deploymentCatalog.FindByIdAsync(id);

        if (deployment == null)
        {
            return NotFound();
        }

        if (deployment.IsReadOnly)
        {
            TempData["ErrorMessage"] = "Deployments defined in appsettings are read-only and cannot be deleted from the UI.";

            return RedirectToAction(nameof(Index));
        }

        await _deploymentCatalog.DeleteAsync(deployment);

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdownsAsync(AIDeploymentViewModel model)
    {
        var selectedProvider = string.IsNullOrWhiteSpace(model.ClientName)
            ? null
            : model.ClientName;

        var connections = await _connectionCatalog.GetAllAsync();

        model.Connections = connections
            .Where(connection => selectedProvider is null || string.Equals(connection.ClientName, selectedProvider, StringComparison.OrdinalIgnoreCase))
            .OrderBy(connection => connection.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(connection => connection.DisplayText ?? connection.Name, StringComparer.OrdinalIgnoreCase)
            .Select(connection =>
            {
                var connectionAlias = connection.DisplayText ?? connection.Name;
                var displayName = selectedProvider is null
                    ? $"{connectionAlias} ({connection.ClientName})"
                    : connectionAlias;

                return new SelectListItem(displayName, connection.Name);
            })
            .ToList();
        model.Providers = _providers;
        model.AuthenticationTypes = _authTypes;
        model.Types = Enum.GetValues<AIDeploymentType>()
            .Where(static type => type != AIDeploymentType.None)
            .Select(static t => new SelectListItem(t.ToString(), t.ToString()))
            .ToList();
    }

    private async Task ValidateUniqueNameAsync(string technicalName, string currentItemId = null)
    {
        if (string.IsNullOrWhiteSpace(technicalName))
        {
            return;
        }

        var existing = await _deploymentStore.FindByNameAsync(technicalName);
        if (existing != null && !string.Equals(existing.ItemId, currentItemId, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(AIDeploymentViewModel.TechnicalName), "Technical name must be unique across appsettings and UI deployments.");
        }
    }
}
