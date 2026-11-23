using System.Security.Claims;
using CrestApps.Core.AI.Profiles;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Helpers that filter user-selectable agents through <see cref="IAIAgentAccessEvaluator"/>.
/// </summary>
/// <remarks>
/// Agent pickers use <see cref="GetAuthorizedSelectableAgentsAsync"/> so they only list agents the user may use.
/// Save handlers use <see cref="GetAuthorizedSelectableAgentNamesAsync"/> to drop any stored agent name the user
/// may not use. Only user-selectable agents are considered (see
/// <see cref="AIAgentProfileExtensions.IsUserSelectableAgent"/>); always-available and system agents are never
/// passed to the evaluator.
/// </remarks>
public static class AIAgentAccessEvaluatorExtensions
{
    /// <summary>
    /// Returns the user-selectable agents the <paramref name="user"/> may use, in their original order.
    /// </summary>
    /// <param name="evaluator">The agent access evaluator.</param>
    /// <param name="agents">The agent profiles to filter.</param>
    /// <param name="user">The current user principal.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<IReadOnlyList<AIProfile>> GetAuthorizedSelectableAgentsAsync(
        this IAIAgentAccessEvaluator evaluator,
        IEnumerable<AIProfile> agents,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluator);

        var authorized = new List<AIProfile>();

        foreach (var agent in agents ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!agent.IsUserSelectableAgent() || string.IsNullOrWhiteSpace(agent.Name))
            {
                continue;
            }

            if (await evaluator.IsAuthorizedAsync(user, agent))
            {
                authorized.Add(agent);
            }
        }

        return authorized;
    }

    /// <summary>
    /// Returns the names of the user-selectable agents the <paramref name="user"/> may use.
    /// Use this on save to drop any stored agent name the user may not use.
    /// </summary>
    /// <param name="evaluator">The agent access evaluator.</param>
    /// <param name="agents">The agent profiles to filter.</param>
    /// <param name="user">The current user principal.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<HashSet<string>> GetAuthorizedSelectableAgentNamesAsync(
        this IAIAgentAccessEvaluator evaluator,
        IEnumerable<AIProfile> agents,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var authorized = await evaluator.GetAuthorizedSelectableAgentsAsync(agents, user, cancellationToken);

        return authorized
            .Select(agent => agent.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
