using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Applies the selected reasoning effort to <see cref="ChatOptions.Reasoning"/> so every provider
/// adapter that understands the standard reasoning options receives the value.
/// </summary>
public sealed class ReasoningEffortModelParameterBinder : IAIModelParameterBinder
{
    /// <inheritdoc/>
    public string ParameterName
        => AIModelParameterNames.ReasoningEffort;

    /// <inheritdoc/>
    public Task BindAsync(AIModelParameterBindingContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Enum.TryParse<ReasoningEffort>(context.Value, ignoreCase: true, out var effort))
        {
            return Task.CompletedTask;
        }

        var reasoning = context.ChatOptions.Reasoning ?? new ReasoningOptions();
        reasoning.Effort = effort;
        context.ChatOptions.Reasoning = reasoning;

        return Task.CompletedTask;
    }
}
