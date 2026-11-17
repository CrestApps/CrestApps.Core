using System.Security.Claims;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Provides extension methods for <see cref="IAIToolAccessEvaluator"/>.
/// </summary>
public static class AIToolAccessEvaluatorExtensions
{
    /// <summary>
    /// Determines whether the specified user is allowed to use a registered tool: when the evaluator authorizes
    /// the tool's name, or one of its aliases. Access that was granted for an earlier name of the tool therefore
    /// keeps granting access after the tool is renamed.
    /// </summary>
    /// <param name="evaluator">The tool access evaluator.</param>
    /// <param name="user">The current user principal. May be <c>null</c> for anonymous requests.</param>
    /// <param name="toolName">The name the tool is registered under.</param>
    /// <param name="definition">The definition of the tool, or <see langword="null"/> when the tool has none.</param>
    /// <returns><c>true</c> if the user is authorized for the name or for an alias; otherwise <c>false</c>.</returns>
    public static async Task<bool> IsAuthorizedAsync(
        this IAIToolAccessEvaluator evaluator,
        ClaimsPrincipal user,
        string toolName,
        AIToolDefinitionEntry definition)
    {
        ArgumentNullException.ThrowIfNull(evaluator);

        if (await evaluator.IsAuthorizedAsync(user, toolName))
        {
            return true;
        }

        if (definition is null)
        {
            return false;
        }

        foreach (var alias in definition.Aliases)
        {
            if (await evaluator.IsAuthorizedAsync(user, alias))
            {
                return true;
            }
        }

        return false;
    }
}
