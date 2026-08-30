#nullable enable
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Startup.Shared.Realtime;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Mvc.Web.Controllers;

/// <summary>
/// A test harness for the realtime (speech-to-speech) client. The <see cref="Index"/> page captures microphone
/// audio in the browser and streams it over a WebSocket to <see cref="Stream"/>, which delegates to the shared
/// <see cref="RealtimeVoiceBridge"/> to bridge to a provider realtime session.
/// </summary>
public sealed class RealtimeController : Controller
{
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _clientFactory;
    private readonly ILogger<RealtimeController> _logger;

    public RealtimeController(
        IAIDeploymentManager deploymentManager,
        IAIClientFactory clientFactory,
        ILogger<RealtimeController> logger)
    {
        _deploymentManager = deploymentManager;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Renders the test page listing the deployments that support realtime.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var deployments = await _deploymentManager.GetByPurposeAsync(AIDeploymentPurpose.Realtime, cancellationToken);

        return View(deployments.OrderBy(deployment => deployment.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// The WebSocket bridge endpoint. No HTTP-method attribute is applied deliberately: over HTTP/2 the WebSocket
    /// handshake arrives as an extended <c>CONNECT</c> request (RFC 8441), not a <c>GET</c>.
    /// </summary>
    public Task Stream(string deploymentName, string? voice, string? instructions, CancellationToken cancellationToken)
    {
        return RealtimeVoiceBridge.HandleAsync(HttpContext, deploymentName, voice, instructions, _deploymentManager, _clientFactory, _logger, cancellationToken);
    }
}
