using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.Controllers;

[Area("AIChat")]
[Authorize(Policy = "Admin")]
public sealed class AIChatController : Controller
{
    private readonly IAIProfileManager _profileManager;
    private readonly IAIChatSessionManager _sessionManager;
    private readonly IAIChatSessionPromptStore _promptStore;
    private readonly IAIDeploymentManager _deploymentManager;

    public AIChatController(
        IAIProfileManager profileManager,
        IAIChatSessionManager sessionManager,
        IAIChatSessionPromptStore promptStore,
        IAIDeploymentManager deploymentManager)
    {
        _profileManager = profileManager;
        _sessionManager = sessionManager;
        _promptStore = promptStore;
        _deploymentManager = deploymentManager;
    }

    public async Task<IActionResult> Chat(string sessionId, string profileId = null)
    {
        AIChatSession session = null;
        IReadOnlyList<AIChatSessionPrompt> prompts = [];
        AIProfile profile = null;

        if (!string.IsNullOrEmpty(sessionId))
        {
            session = await _sessionManager.FindByIdAsync(sessionId);

            if (session == null)
            {
                return NotFound();
            }

            profile = await _profileManager.FindByIdAsync(session.ProfileId);

            if (profile == null)
            {
                return NotFound();
            }

            prompts = await _promptStore.GetPromptsAsync(sessionId);
        }
        else if (!string.IsNullOrEmpty(profileId))
        {
            profile = await _profileManager.FindByIdAsync(profileId);

            if (profile == null)
            {
                return NotFound();
            }
        }
        else
        {
            return RedirectToAction("Index", "AIProfile", new { area = "AI" });
        }

        ViewData["Session"] = session;
        ViewData["Prompts"] = prompts;
        ViewData["AllowImageUploads"] = await AllowSessionImageUploadsAsync(profile);

        return View(profile);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSession(string profileId)
    {
        var profile = await _profileManager.FindByIdAsync(profileId);

        if (profile == null)
        {
            return NotFound();
        }

        // Navigate to the chat page without creating a session. The session
        // will be created when the user sends their first message via SignalR.
        return RedirectToAction(nameof(Chat), new { profileId = profile.ItemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSession(string sessionId, string returnProfileId = null)
    {
        await _sessionManager.DeleteAsync(sessionId);

        if (!string.IsNullOrEmpty(returnProfileId))
        {
            return RedirectToAction(nameof(Sessions), new { profileId = returnProfileId });
        }

        return RedirectToAction("Index", "AIProfile", new { area = "AI" });
    }

    public async Task<IActionResult> Sessions(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
        {
            return RedirectToAction("Index", "AIProfile", new { area = "AI" });
        }

        var profile = await _profileManager.FindByIdAsync(profileId);

        if (profile == null)
        {
            return NotFound();
        }

        var sessions = await _sessionManager.PageAsync(1, 200, new AIChatSessionQueryContext
        {
            ProfileId = profileId,
            Sorted = true,
        });

        ViewData["Profile"] = profile;

        return View(sessions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAllSessions(string profileId)
    {
        if (!string.IsNullOrEmpty(profileId))
        {
            await _sessionManager.DeleteAllAsync(profileId);
        }

        return RedirectToAction(nameof(Sessions), new { profileId });
    }

    public async Task<IActionResult> Test(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
        {
            return RedirectToAction("Index", "AIProfile", new { area = "AI" });
        }

        var profile = await _profileManager.FindByIdAsync(profileId);

        if (profile == null)
        {
            return NotFound();
        }

        if (profile.Type != AIProfileType.Utility && profile.Type != AIProfileType.Agent)
        {
            return RedirectToAction("Index", "AIProfile", new { area = "AI" });
        }

        return View(profile);
    }

    private async Task<bool> AllowSessionImageUploadsAsync(AIProfile profile)
    {
        if (!profile.TryGet<AIProfileSessionDocumentsMetadata>(out var metadata) || !metadata.AllowSessionImageUploads)
        {
            return false;
        }

        var visionDeployment = await _deploymentManager.ResolveOrDefaultAsync(AIDeploymentPurpose.Vision);

        return visionDeployment != null;
    }
}
