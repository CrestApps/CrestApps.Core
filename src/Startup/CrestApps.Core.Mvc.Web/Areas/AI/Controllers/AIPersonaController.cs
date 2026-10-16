using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Models;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Controllers;

/// <summary>
/// Lists, creates, edits and deletes AI personas. Personas are an opt-in feature, so every action answers
/// <c>404</c> when the host did not add it.
/// </summary>
[Area("AI")]
[Authorize(Policy = "Admin")]
public sealed class AIPersonaController : Controller
{
    private readonly IAIPersonaManager _personaManager;
    private readonly INamedCatalog<AIPersona> _personaCatalog;
    private readonly IAIDeploymentManager _deploymentManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIPersonaController"/> class.
    /// </summary>
    /// <param name="services">The service provider, used to find the persona services when the host registered them.</param>
    /// <param name="deploymentManager">The deployment manager, used to list the deployments a voice can come from.</param>
    public AIPersonaController(
        IServiceProvider services,
        IAIDeploymentManager deploymentManager)
    {
        _personaManager = services.GetService<IAIPersonaManager>();
        _personaCatalog = services.GetService<INamedCatalog<AIPersona>>();
        _deploymentManager = deploymentManager;
    }

    /// <summary>
    /// Lists every persona, saved and file-based.
    /// </summary>
    public async Task<IActionResult> Index()
    {
        if (_personaManager is null)
        {
            return NotFound();
        }

        var savedIds = await GetSavedIdsAsync();
        var personas = (await _personaManager.GetAllAsync())
            .OrderBy(persona => persona.DisplayText ?? persona.Name, StringComparer.OrdinalIgnoreCase)
            .Select(persona => AIPersonaViewModel.FromPersona(persona, !savedIds.Contains(persona.ItemId)))
            .ToList();

        return View(personas);
    }

    /// <summary>
    /// Shows the form for a new persona.
    /// </summary>
    public async Task<IActionResult> Create()
    {
        if (_personaManager is null)
        {
            return NotFound();
        }

        var model = new AIPersonaViewModel();
        await PopulateOptionsAsync(model);

        return View(model);
    }

    /// <summary>
    /// Creates a persona.
    /// </summary>
    /// <param name="model">The submitted form.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AIPersonaViewModel model)
    {
        if (_personaManager is null)
        {
            return NotFound();
        }

        if (!await SaveNewAsync(model))
        {
            await PopulateOptionsAsync(model);

            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Shows the form for a persona. A file-based persona opens as a copy that, once saved, replaces the file.
    /// </summary>
    /// <param name="id">The persona identifier.</param>
    public async Task<IActionResult> Edit(string id)
    {
        if (_personaManager is null || string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        var persona = await _personaManager.FindByIdAsync(id);

        if (persona is null)
        {
            return NotFound();
        }

        var savedIds = await GetSavedIdsAsync();
        var model = AIPersonaViewModel.FromPersona(persona, !savedIds.Contains(persona.ItemId));
        await PopulateOptionsAsync(model);

        return View(model);
    }

    /// <summary>
    /// Saves a persona. A persona that came from a file is saved as a new persona with the same name.
    /// </summary>
    /// <param name="model">The submitted form.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AIPersonaViewModel model)
    {
        if (_personaManager is null)
        {
            return NotFound();
        }

        var existing = string.IsNullOrEmpty(model.ItemId) || _personaCatalog is null
            ? null
            : await _personaCatalog.FindByIdAsync(model.ItemId);

        if (existing is null)
        {
            // A file-based persona: saving it creates the saved copy that replaces the file.
            model.IsFromFile = true;

            if (!await SaveNewAsync(model))
            {
                await PopulateOptionsAsync(model);

                return View(model);
            }

            return RedirectToAction(nameof(Index));
        }

        // The update saves as it applies the form, so the form is checked on a copy first.
        model.Name = existing.Name;
        var candidate = existing.Clone();
        candidate.Instructions = string.IsNullOrWhiteSpace(model.Instructions) ? null : model.Instructions;

        if (!await AddErrorsAsync(candidate))
        {
            await PopulateOptionsAsync(model);

            return View(model);
        }

        await _personaManager.UpdateAsync(existing, model.ToJson());

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Deletes a saved persona. Profiles that link to it carry on without a persona.
    /// </summary>
    /// <param name="id">The persona identifier.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (_personaManager is null || _personaCatalog is null || string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        var persona = await _personaCatalog.FindByIdAsync(id);

        if (persona is null)
        {
            return NotFound();
        }

        await _personaManager.DeleteAsync(persona);

        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> SaveNewAsync(AIPersonaViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "Technical name is required.");

            return false;
        }

        var persona = await _personaManager.NewAsync(model.Name.Trim(), model.ToJson());

        if (!await AddErrorsAsync(persona))
        {
            return false;
        }

        await _personaManager.CreateAsync(persona);

        return true;
    }

    private async Task<bool> AddErrorsAsync(AIPersona persona)
    {
        var result = await _personaManager.ValidateAsync(persona);

        foreach (var error in result.Errors)
        {
            var memberName = error.MemberNames.FirstOrDefault() ?? string.Empty;
            ModelState.AddModelError(memberName, error.ErrorMessage);
        }

        return result.Succeeded;
    }

    private async Task<HashSet<string>> GetSavedIdsAsync()
    {
        var saved = _personaCatalog is null
            ? []
            : await _personaCatalog.GetAllAsync();

        return new HashSet<string>(saved.Select(persona => persona.ItemId), StringComparer.Ordinal);
    }

    private async Task PopulateOptionsAsync(AIPersonaViewModel model)
    {
        // A voice name only means something to the provider it came from, so the persona's voice is picked from a
        // deployment that can speak and that deployment is remembered with it.
        var realtimeGroup = new SelectListGroup { Name = "Realtime (speech-to-speech)" };
        var speechGroup = new SelectListGroup { Name = "Text-to-speech" };
        var realtimeDeployments = (await _deploymentManager.GetAllBySlotAsync(AIDeploymentSlotNames.Realtime)).ToList();
        var speechDeployments = await _deploymentManager.GetAllBySlotAsync(AIDeploymentSlotNames.TextToSpeech);

        model.RealtimeDeploymentNames = realtimeDeployments.Select(deployment => deployment.Name).ToArray();
        model.VoiceDeployments = realtimeDeployments
            .Select(deployment => new SelectListItem(BuildDeploymentLabel(deployment), deployment.Name) { Group = realtimeGroup })
            .Concat(speechDeployments.Select(deployment => new SelectListItem(BuildDeploymentLabel(deployment), deployment.Name) { Group = speechGroup }))
            .ToList();
    }

    private static string BuildDeploymentLabel(AIDeployment deployment)
    {
        return string.Equals(deployment.Name, deployment.ModelName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(deployment.ModelName)
            ? deployment.Name
            : $"{deployment.Name} ({deployment.ModelName})";
    }
}
