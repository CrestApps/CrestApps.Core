using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
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

    public AIChatController(
        IAIProfileManager profileManager,
        IAIChatSessionManager sessionManager,
        IAIChatSessionPromptStore promptStore)
    {
        _profileManager = profileManager;
        _sessionManager = sessionManager;
        _promptStore = promptStore;
    }

    public async Task<IActionResult> Chat(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return RedirectToAction("Index", "AIProfile", new { area = "AI" });
        }

        var session = await _sessionManager.FindByIdAsync(sessionId);

        if (session == null)
        {
            return NotFound();
        }

        var profile = await _profileManager.FindByIdAsync(session.ProfileId);

        if (profile == null)
        {
            return NotFound();
        }

        var prompts = await _promptStore.GetPromptsAsync(sessionId);

        ViewData["Session"] = session;
        ViewData["Prompts"] = prompts;

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

        var session = await _sessionManager.NewAsync(profile, new NewAIChatSessionContext());

        session.Title = profile.DisplayText ?? profile.Name;

        await _sessionManager.SaveAsync(session);

        return RedirectToAction(nameof(Chat), new { sessionId = session.SessionId });
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
}
