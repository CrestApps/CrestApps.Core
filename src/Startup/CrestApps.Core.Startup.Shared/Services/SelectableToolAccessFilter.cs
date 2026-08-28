using System.Security.Claims;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.Startup.Shared.Services;

/// <summary>
/// Filters the registered <em>listable</em> (user-selectable) tools to those the current user is
/// authorized to use, via <see cref="IAIToolAccessEvaluator"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a reference for consuming applications: the AI Profile, AI Template (profile source), and
/// Chat Interaction editors call it so their tool pickers only ever show tools the author is allowed
/// to use, and so the persisted selection is validated against the same check on save. The default
/// <see cref="IAIToolAccessEvaluator"/> permits every tool, so out of the box nothing is filtered —
/// replace it with an authorization-aware implementation to enforce a real permission model.
/// </para>
/// <para>
/// Only listable tools are considered here; system tools are auto-injected by the orchestrator and
/// hidden/dependency tools are never user-selectable, so neither appears in these pickers.
/// </para>
/// </remarks>
public static class SelectableToolAccessFilter
{
    /// <summary>
    /// Returns the selectable tool definitions the <paramref name="user"/> is authorized to use.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, AIToolDefinitionEntry>> GetAuthorizedSelectableToolsAsync(
        AIToolDefinitionOptions toolOptions,
        IAIToolAccessEvaluator accessEvaluator,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolOptions);
        ArgumentNullException.ThrowIfNull(accessEvaluator);

        var authorized = new Dictionary<string, AIToolDefinitionEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in toolOptions.GetSelectableTools())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await accessEvaluator.IsAuthorizedAsync(user, tool.Key))
            {
                authorized.Add(tool.Key, tool.Value);
            }
        }

        return authorized;
    }

    /// <summary>
    /// Returns the names of the selectable tools the <paramref name="user"/> is authorized to use.
    /// Use this on save to reject any persisted tool name the caller is not allowed to select.
    /// </summary>
    public static async Task<HashSet<string>> GetAuthorizedSelectableToolNamesAsync(
        AIToolDefinitionOptions toolOptions,
        IAIToolAccessEvaluator accessEvaluator,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var authorized = await GetAuthorizedSelectableToolsAsync(toolOptions, accessEvaluator, user, cancellationToken);

        return new HashSet<string>(authorized.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
