using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Controllers;

/// <summary>
/// Searches the voice library of a speech deployment's provider (ElevenLabs, Cartesia) and, for a provider that
/// supports it, clones and deletes voices.
/// </summary>
[Area("AI")]
[Authorize(Policy = "Admin")]
public sealed class VoiceLibraryController : Controller
{
    private const long MaxSampleBytes = 25 * 1024 * 1024;

    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IVoiceLibraryResolver _libraryResolver;
    private readonly ILogger<VoiceLibraryController> _logger;

    public VoiceLibraryController(
        IAIDeploymentManager deploymentManager,
        IVoiceLibraryResolver libraryResolver,
        ILogger<VoiceLibraryController> logger)
    {
        _deploymentManager = deploymentManager;
        _libraryResolver = libraryResolver;
        _logger = logger;
    }

    /// <summary>
    /// Shows the deployment picker and, once a deployment is chosen, the voices that match the search.
    /// </summary>
    public async Task<IActionResult> Index(VoiceLibraryViewModel model, CancellationToken cancellationToken)
    {
        model ??= new VoiceLibraryViewModel();
        await PopulateDeploymentsAsync(model);

        if (string.IsNullOrWhiteSpace(model.Deployment))
        {
            return View(model);
        }

        var library = await GetLibraryAsync(model.Deployment, cancellationToken);

        if (library is null)
        {
            return View(model);
        }

        model.HasLibrary = true;
        model.SupportsCloning = library.SupportsCloning;

        try
        {
            var result = await library.SearchAsync(new VoiceSearchRequest
            {
                Query = string.IsNullOrWhiteSpace(model.Query) ? null : model.Query.Trim(),
                Language = string.IsNullOrWhiteSpace(model.Language) ? null : model.Language.Trim(),
                Gender = model.Gender is null or SpeechVoiceGender.Unknown ? null : model.Gender,
                Cursor = string.IsNullOrWhiteSpace(model.Cursor) ? null : model.Cursor,
            }, cancellationToken);

            model.Voices = result.Voices;
            model.NextCursor = result.NextCursor;
            model.HasSearched = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Searching the voice library of deployment '{Deployment}' failed.", model.Deployment);
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        return View(model);
    }

    /// <summary>
    /// Clones a voice from the uploaded recordings.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(150 * 1024 * 1024)]
    public async Task<IActionResult> Clone(string deployment, string name, string language, string description, List<IFormFile> files, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = "A voice name is required.";

            return RedirectToAction(nameof(Index), new { deployment });
        }

        files = (files ?? []).Where(file => file is { Length: > 0 }).ToList();

        if (files.Count == 0)
        {
            TempData["ErrorMessage"] = "Choose at least one audio recording to clone from.";

            return RedirectToAction(nameof(Index), new { deployment });
        }

        if (files.Any(file => file.Length > MaxSampleBytes))
        {
            TempData["ErrorMessage"] = "Each recording must be smaller than 25 MB.";

            return RedirectToAction(nameof(Index), new { deployment });
        }

        var library = await GetLibraryAsync(deployment, cancellationToken);

        if (library is null || !library.SupportsCloning)
        {
            TempData["ErrorMessage"] = "The selected deployment's provider cannot clone voices.";

            return RedirectToAction(nameof(Index), new { deployment });
        }

        var samples = new List<VoiceCloneSample>(files.Count);

        foreach (var file in files)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);

            samples.Add(new VoiceCloneSample
            {
                FileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                Content = stream.ToArray(),
            });
        }

        try
        {
            var voice = await library.CloneAsync(new VoiceCloneRequest
            {
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
                Samples = samples,
            }, cancellationToken);

            TempData["SuccessMessage"] = $"Voice '{voice.Name}' was created. Its id is {voice.Id}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Cloning a voice with deployment '{Deployment}' failed.", deployment);
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { deployment });
    }

    /// <summary>
    /// Deletes a voice that was created by cloning.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string deployment, string voiceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            return RedirectToAction(nameof(Index), new { deployment });
        }

        var library = await GetLibraryAsync(deployment, cancellationToken);

        if (library is null || !library.SupportsCloning)
        {
            TempData["ErrorMessage"] = "The selected deployment's provider cannot delete voices.";

            return RedirectToAction(nameof(Index), new { deployment });
        }

        try
        {
            await library.DeleteAsync(voiceId, cancellationToken);
            TempData["SuccessMessage"] = "The voice was deleted.";
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Deleting a voice with deployment '{Deployment}' failed.", deployment);
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { deployment });
    }

    private async Task<IVoiceLibrary> GetLibraryAsync(string deploymentName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            return null;
        }

        var deployment = await _deploymentManager.FindByNameAsync(deploymentName, cancellationToken);

        return deployment is null
            ? null
            : await _libraryResolver.GetLibraryAsync(deployment, cancellationToken);
    }

    private async Task PopulateDeploymentsAsync(VoiceLibraryViewModel model)
    {
        model.Deployments = (await _deploymentManager.GetAllBySlotAsync(AIDeploymentSlotNames.TextToSpeech))
            .OrderBy(deployment => deployment.Name, StringComparer.OrdinalIgnoreCase)
            .Select(deployment => new SelectListItem($"{deployment.Name} ({deployment.ClientName})", deployment.Name))
            .ToList();
    }
}
