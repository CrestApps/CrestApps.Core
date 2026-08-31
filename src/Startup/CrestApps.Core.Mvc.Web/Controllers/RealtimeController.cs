#nullable enable
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Startup.Shared.Realtime;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Mvc.Web.Controllers;

/// <summary>
/// A test harness for the realtime (speech-to-speech) client. The <see cref="Index"/> page captures microphone
/// audio in the browser and streams it over a WebSocket to <see cref="Stream"/>, which delegates to the shared
/// <see cref="RealtimeVoiceBridge"/> to bridge to a provider realtime session — either a raw deployment (bare
/// instructions) or a full realtime-mode chat profile (system message + tools + RAG).
/// </summary>
public sealed class RealtimeController : Controller
{
    private readonly IRealtimeCapabilityResolver _realtimeResolver;
    private readonly IAIClientFactory _clientFactory;
    private readonly IRealtimeVoiceResolver _voiceResolver;
    private readonly IAIProfileManager _profileManager;
    private readonly IRealtimeOrchestrator _orchestrator;
    private readonly ILogger<RealtimeController> _logger;

    public RealtimeController(
        IRealtimeCapabilityResolver realtimeResolver,
        IAIClientFactory clientFactory,
        IRealtimeVoiceResolver voiceResolver,
        IAIProfileManager profileManager,
        IRealtimeOrchestrator orchestrator,
        ILogger<RealtimeController> logger)
    {
        _realtimeResolver = realtimeResolver;
        _clientFactory = clientFactory;
        _voiceResolver = voiceResolver;
        _profileManager = profileManager;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Renders the test page listing the deployments and realtime-mode chat profiles that support realtime.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var deployments = (await _realtimeResolver.GetRealtimeDeploymentsAsync(cancellationToken))
            .OrderBy(deployment => deployment.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Voices are resolved from the providers. All realtime deployments in practice share the same voice set,
        // so the distinct union across deployments is used to populate the selector.
        var voiceSets = await Task.WhenAll(deployments.Select(deployment => _voiceResolver.GetVoicesAsync(deployment)));

        ViewData["RealtimeVoices"] = voiceSets
            .SelectMany(voices => voices)
            .GroupBy(voice => voice.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        ViewData["RealtimeProfiles"] = (await _profileManager.GetAsync(AIProfileType.Chat, cancellationToken))
            .Where(profile => profile.TryGetSettings<ChatModeProfileSettings>(out var chatMode) && chatMode.ChatMode == ChatMode.Realtime)
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return View(deployments);
    }

    /// <summary>
    /// The WebSocket bridge endpoint. No HTTP-method attribute is applied deliberately: over HTTP/2 the WebSocket
    /// handshake arrives as an extended <c>CONNECT</c> request (RFC 8441), not a <c>GET</c>.
    /// When <paramref name="profileId"/> is supplied the session runs through the orchestrator (tools + RAG);
    /// otherwise it bridges the raw deployment with the supplied instructions.
    /// </summary>
    public async Task Stream(string? deploymentName, string? profileId, string? voice, string? instructions, string? language, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            var profile = await _profileManager.FindByIdAsync(profileId, cancellationToken);

            await RealtimeVoiceBridge.HandleProfileAsync(HttpContext, profile, voice, language, _orchestrator, _logger, cancellationToken);

            return;
        }

        await RealtimeVoiceBridge.HandleAsync(HttpContext, deploymentName, voice, instructions, _realtimeResolver, _clientFactory, _logger, cancellationToken);
    }
}
