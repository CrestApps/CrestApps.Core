using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Mvc.Web.Controllers;

/// <summary>
/// Serves the WebMCP playground, a fake store that exposes tools through the standard WebMCP API for testing agents.
/// </summary>
[Authorize]
public sealed class WebMcpController : Controller
{
    /// <summary>
    /// Shows the WebMCP playground.
    /// </summary>
    /// <returns>The playground page.</returns>
    [HttpGet("webmcp/playground")]
    public IActionResult Playground()
    {
        return View();
    }
}
