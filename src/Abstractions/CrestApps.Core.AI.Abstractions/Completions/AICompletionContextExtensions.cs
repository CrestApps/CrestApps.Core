using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Provides extension methods for <see cref="AICompletionContext"/>.
/// </summary>
public static class AICompletionContextExtensions
{
    /// <summary>
    /// Copies the model parameter values held by the given metadata onto the completion context.
    /// Empty values are ignored so a stored blank never overrides a deployment default.
    /// </summary>
    /// <param name="context">The completion context to populate.</param>
    /// <param name="metadata">The metadata holding the selected model parameter values.</param>
    public static void ApplyModelParameters(this AICompletionContext context, AIDeploymentParametersMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (metadata?.Values is not { Count: > 0 })
        {
            return;
        }

        foreach (var (name, value) in metadata.Values)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            context.ModelParameters[name] = value;
        }
    }
}
