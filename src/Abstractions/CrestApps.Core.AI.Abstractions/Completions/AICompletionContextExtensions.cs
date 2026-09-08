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
