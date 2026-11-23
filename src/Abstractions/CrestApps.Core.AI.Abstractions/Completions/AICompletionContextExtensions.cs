using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Provides extension methods for <see cref="AICompletionContext"/>.
/// </summary>
public static class AICompletionContextExtensions
{
    /// <summary>
    /// Copies the chat and utility model parameter values held by the given metadata onto the completion
    /// context. Empty values are ignored so a stored blank never overrides a deployment default.
    /// </summary>
    /// <param name="context">The completion context to populate.</param>
    /// <param name="metadata">The metadata holding the selected model parameter values.</param>
    public static void ApplyModelParameters(this AICompletionContext context, AIDeploymentParametersMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (metadata is null)
        {
            return;
        }

        Copy(metadata.Values, context.ModelParameters);
        Copy(metadata.UtilityValues, context.UtilityModelParameters);
    }

    /// <summary>
    /// Gets the approval policy of the request (see <see cref="AICompletionContextKeys.ToolApprovalPolicy"/>).
    /// </summary>
    /// <param name="context">The completion context of the request.</param>
    /// <returns>The policy, or <see langword="null"/> when every call that requires an approval asks.</returns>
    public static ToolApprovalPolicy GetToolApprovalPolicy(this AICompletionContext context)
    {
        return context is not null &&
            context.AdditionalProperties.TryGetValue(AICompletionContextKeys.ToolApprovalPolicy, out var value)
            ? value as ToolApprovalPolicy
            : null;
    }

    /// <summary>
    /// Sets the approval policy of the request, or removes it when <paramref name="policy"/> is
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="context">The completion context of the request.</param>
    /// <param name="policy">The policy, or <see langword="null"/> to ask before every call.</param>
    public static void SetToolApprovalPolicy(this AICompletionContext context, ToolApprovalPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (policy is null)
        {
            context.AdditionalProperties.Remove(AICompletionContextKeys.ToolApprovalPolicy);
        }
        else
        {
            context.AdditionalProperties[AICompletionContextKeys.ToolApprovalPolicy] = policy;
        }
    }

    private static void Copy(Dictionary<string, string> source, Dictionary<string, string> destination)
    {
        if (source is not { Count: > 0 })
        {
            return;
        }

        foreach (var (name, value) in source)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            destination[name] = value;
        }
    }
}
