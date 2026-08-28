using System.Security.Claims;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Evaluates whether a given user is authorized to use a specific AI tool.
/// </summary>
/// <remarks>
/// <para>
/// This is invoked only for <strong>Chat Interaction</strong> requests, and only for
/// <em>listable</em> (user-selectable) tools. A Chat Interaction persists the tool names chosen in
/// its settings, so a caller who tampers with those settings could reference a selectable tool they
/// were never granted; re-checking each listable tool at send time closes that gap. System tools
/// (auto-injected) and hidden/dependency tools bypass the check.
/// </para>
/// <para>
/// AI <strong>Sessions</strong> do <em>not</em> use this evaluator: a session runs an AI Profile
/// exactly as it was configured (the profile is the authorization boundary), which is important
/// because sessions may be anonymous. Enforce which tools a profile or Chat Interaction may contain
/// where they are configured; this evaluator is the runtime backstop for the Chat Interaction case.
/// </para>
/// </remarks>
public interface IAIToolAccessEvaluator
{
    /// <summary>
    /// Determines whether the specified user is allowed to invoke the given tool.
    /// </summary>
    /// <param name="user">The current user principal. May be <c>null</c> for anonymous requests.</param>
    /// <param name="toolName">The name of the AI tool to authorize.</param>
    /// <returns><c>true</c> if the user is authorized; otherwise <c>false</c>.</returns>
    Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName);
}
