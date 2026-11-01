using System.Security.Claims;

namespace CrestApps.Core.AI.WebMcp.Services;

/// <summary>
/// Decides whether a user may use the WebMCP tool endpoints at all. Each tool is also checked against the user's
/// per-tool permission. Hosts replace the default, which allows every signed-in user, to require a role or permission.
/// </summary>
public interface IWebMcpAccessEvaluator
{
    /// <summary>
    /// Determines whether the user may list and call WebMCP tools.
    /// </summary>
    /// <param name="user">The user, or <see langword="null"/> when there is none.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the user may use WebMCP tools; otherwise <see langword="false"/>.</returns>
    Task<bool> CanUseAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
