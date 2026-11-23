using CrestApps.Core.AI.Models;

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
    /// Determines whether the profile of the request was set to run its tools without asking, so the tools that
    /// require an approval run at once (see <see cref="AICompletionContextKeys.RunToolsWithoutApproval"/>).
    /// </summary>
    /// <param name="context">The completion context of the request.</param>
    /// <returns><see langword="true"/> when the tools run without asking; otherwise <see langword="false"/>.</returns>
    public static bool RunsToolsWithoutApproval(this AICompletionContext context)
    {
        return context is not null && context.AdditionalProperties.TryGetValue(AICompletionContextKeys.RunToolsWithoutApproval, out var value) && value is true;
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
