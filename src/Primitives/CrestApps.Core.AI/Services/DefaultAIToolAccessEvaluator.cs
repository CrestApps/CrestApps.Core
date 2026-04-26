using System.Security.Claims;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default implementation that permits all tool access.
/// Replace with an authorization-aware implementation for fine-grained control.
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
