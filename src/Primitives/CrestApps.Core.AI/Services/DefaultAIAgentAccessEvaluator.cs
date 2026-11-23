using System.Security.Claims;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default implementation that allows every agent.
/// Replace with an authorization-aware implementation to decide, per user, which agents may be used.
/// </summary>
internal sealed class DefaultAIAgentAccessEvaluator : IAIAgentAccessEvaluator
{
    /// <summary>
    /// Determines whether authorized.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="agent">The agent profile.</param>
    public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, AIProfile agent)
    {
        return Task.FromResult(true);
    }
}
