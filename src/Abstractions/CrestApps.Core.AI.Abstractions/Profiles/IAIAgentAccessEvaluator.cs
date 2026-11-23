using System.Security.Claims;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// Decides whether a given user may use a specific AI agent.
/// </summary>
/// <remarks>
/// <para>
/// An agent runs on its own profile, and an agent allowed to run tools runs its own tools there, so whoever
/// may use such an agent may also use the tools it carries. This hook lets a host decide, per user, which
/// agents may be picked and used.
/// </para>
/// <para>
/// It is checked where a user picks agents (the AI Profile, AI Template and Chat Interaction editors), where
/// a Chat Interaction's agent selection is saved, and at run time for every <strong>Chat Interaction</strong>
/// request. A Chat Interaction stores the agent names its author picked, so a caller who tampers with those
/// settings could reference an agent they may not use; the run-time check leaves such an agent out.
/// </para>
/// <para>
/// Only user-selectable agents are checked. Always-available agents and system agents are included by the
/// framework and are never passed to this evaluator. AI <strong>Sessions</strong> do <em>not</em> use it at
/// run time: a session runs an AI Profile exactly as it was configured, so the profile is the authorization
/// boundary.
/// </para>
/// <para>
/// The default implementation allows every agent. Replace it to enforce a per-user permission model.
/// </para>
/// </remarks>
public interface IAIAgentAccessEvaluator
{
    /// <summary>
    /// Determines whether the specified user may use the given agent.
    /// </summary>
    /// <param name="user">The current user principal. May be <c>null</c> when there is no caller.</param>
    /// <param name="agent">The agent profile to authorize.</param>
    /// <returns><c>true</c> if the user may use the agent; otherwise <c>false</c>.</returns>
    Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, AIProfile agent);
}
