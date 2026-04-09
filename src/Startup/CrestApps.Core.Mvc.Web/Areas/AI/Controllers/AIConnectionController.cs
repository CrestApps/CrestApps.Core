using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Controllers;

[Area("AI")]
[Authorize(Policy = "Admin")]
public sealed class AIConnectionController : Controller
{
    private readonly INamedSourceCatalog<AIProviderConnection> _catalog;
    private static readonly List<SelectListItem> _providers = [new("OpenAI", "OpenAI"), new("Azure OpenAI", "Azure"), new("Azure AI Inference (GitHub Models)", "AzureAIInference"), new("Ollama", "Ollama"),];
    private static readonly List<SelectListItem> _authTypes = [new("API Key", "ApiKey"), new("Default Azure Credential", "Default"), new("Managed Identity", "ManagedIdentity"),];
    public AIConnectionController(INamedSourceCatalog<AIProviderConnection> catalog)
    {
        _catalog = catalog;
    }

    public async Task<IActionResult> Index()
    {
        var connections = await _catalog.GetAllAsync();
        var models = connections.Select(connection =>
        {
            var model = AIConnectionViewModel.FromConnection(connection);
            model.IsReadOnly = AIConfigurationRecordIds.IsConfigurationConnectionId(connection.ItemId);
            return model;
        }).ToList();

        return View(models.OrderBy(static model => model.DisplayText ?? model.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public IActionResult Create()
    {
        var model = new AIConnectionViewModel
        {
            Providers = _providers,
            AuthenticationTypes = _authTypes,
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AIConnectionViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "Name is required.");
        }

        if (string.IsNullOrWhiteSpace(model.Source))
        {
            ModelState.AddModelError(nameof(model.Source), "Provider is required.");
        }

        await ValidateUniqueNameAsync(model.Name);

        if (!ModelState.IsValid)
        {
            model.Providers = _providers;
            model.AuthenticationTypes = _authTypes;
            return View(model);
        }

        var connection = new AIProviderConnection();
        if (string.IsNullOrEmpty(connection.ItemId))
        {
            connection.ItemId = Guid.NewGuid().ToString("N");
        }

        connection.CreatedUtc = DateTime.UtcNow;
        model.ApplyTo(connection);
        await _catalog.CreateAsync(connection);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        if (AIConfigurationRecordIds.IsConfigurationConnectionId(id))
        {
            TempData["ErrorMessage"] = "Connections defined in appsettings are read-only and cannot be edited from the UI.";
            return RedirectToAction(nameof(Index));
        }

        var connection = await _catalog.FindByIdAsync(id);
        if (connection == null)
        {
            return NotFound();
        }

        if (AIConfigurationRecordIds.IsConfigurationConnectionId(connection.ItemId))
        {
            TempData["ErrorMessage"] = "Connections defined in appsettings are read-only and cannot be edited from the UI.";
            return RedirectToAction(nameof(Index));
        }

        var model = AIConnectionViewModel.FromConnection(connection);
        model.Providers = _providers;
        model.AuthenticationTypes = _authTypes;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AIConnectionViewModel model)
    {
        if (AIConfigurationRecordIds.IsConfigurationConnectionId(model.ItemId))
        {
            TempData["ErrorMessage"] = "Connections defined in appsettings are read-only and cannot be edited from the UI.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "Name is required.");
        }

        await ValidateUniqueNameAsync(model.Name, model.ItemId);

        if (!ModelState.IsValid)
        {
            model.Providers = _providers;
            model.AuthenticationTypes = _authTypes;
            return View(model);
        }

        var existing = await _catalog.FindByIdAsync(model.ItemId);
        if (existing == null)
        {
            return NotFound();
        }

        if (AIConfigurationRecordIds.IsConfigurationConnectionId(existing.ItemId))
        {
            TempData["ErrorMessage"] = "Connections defined in appsettings are read-only and cannot be edited from the UI.";
            return RedirectToAction(nameof(Index));
        }

        model.ApplyTo(existing);
        await _catalog.UpdateAsync(existing);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (AIConfigurationRecordIds.IsConfigurationConnectionId(id))
        {
            TempData["ErrorMessage"] = "Connections defined in appsettings are read-only and cannot be deleted from the UI.";
            return RedirectToAction(nameof(Index));
        }

        var connection = await _catalog.FindByIdAsync(id);
        if (connection == null)
        {
            return NotFound();
        }

        if (AIConfigurationRecordIds.IsConfigurationConnectionId(connection.ItemId))
        {
            TempData["ErrorMessage"] = "Connections defined in appsettings are read-only and cannot be deleted from the UI.";
            return RedirectToAction(nameof(Index));
        }

        await _catalog.DeleteAsync(connection);
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateUniqueNameAsync(string name, string currentItemId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var existing = await _catalog.FindByNameAsync(name);
        if (existing != null && !string.Equals(existing.ItemId, currentItemId, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(AIConnectionViewModel.Name), "Name must be unique across appsettings and UI connections.");
        }
    }
}
