using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Applies the model parameters selected for the current request to the outgoing
/// <see cref="ChatOptions"/>. Only the parameters exposed by the resolved deployment are applied,
/// which guarantees that unsupported parameters are never sent to a provider. Background utility
/// completions apply the values selected for the utility deployment instead of the chat deployment.
/// </summary>
public sealed class ModelParametersAICompletionServiceHandler : IAICompletionServiceHandler
{
    private readonly IAIDeploymentParameterApplier _applier;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelParametersAICompletionServiceHandler"/> class.
    /// </summary>
    /// <param name="applier">The applier that binds the selected values onto the chat options.</param>
    public ModelParametersAICompletionServiceHandler(IAIDeploymentParameterApplier applier)
    {
        _applier = applier;
    }

    /// <inheritdoc/>
    public async Task ConfigureAsync(CompletionServiceConfigureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var scope = context.CompletionContext.IsUtilityCompletion
            ? AIDeploymentParameterScope.Utility
            : AIDeploymentParameterScope.Chat;

        await _applier.ApplyAsync(context.ChatOptions, context.Deployment, context.CompletionContext, scope, cancellationToken);
    }
}
