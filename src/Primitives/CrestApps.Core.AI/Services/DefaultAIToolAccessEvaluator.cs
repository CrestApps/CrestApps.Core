using System.Security.Claims;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default implementation that permits all tool access.
/// Replace with an authorization-aware implementation to enforce per-user access to the
/// listable tools a Chat Interaction may use.
/// </summary>
internal sealed class DefaultAIToolAccessEvaluator : IAIToolAccessEvaluator
{
    /// <summary>
    /// Determines whether authorized.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="toolName">The tool name.</param>
    public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName)
    {
        return Task.FromResult(true);
    }
}
