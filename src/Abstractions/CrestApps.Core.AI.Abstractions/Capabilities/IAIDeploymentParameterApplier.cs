using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Capabilities;

/// <summary>
/// Applies the model parameter values selected for a request onto the outgoing
/// <see cref="ChatOptions"/>. Only the parameters exposed by the resolved deployment are applied, so
/// an unsupported parameter is never sent to a provider. Callers that issue a completion outside the
/// completion pipeline — such as the background utility completions used for planning, data
/// extraction, and post-session processing — use this service so their requests honor the same
/// operator selections as the chat pipeline.
/// </summary>
public interface IAIDeploymentParameterApplier
{
    /// <summary>
    /// Applies the values selected in the given scope to the supplied chat options.
    /// </summary>
    /// <param name="options">The chat options to mutate.</param>
    /// <param name="deployment">The deployment resolved for the request. When <see langword="null"/>, the deployment named by the scope on the completion context is resolved instead.</param>
    /// <param name="completionContext">The completion context holding the selected values.</param>
    /// <param name="scope">The set of selected values to apply.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task ApplyAsync(
        ChatOptions options,
        AIDeployment deployment,
        AICompletionContext completionContext,
        AIDeploymentParameterScope scope,
        CancellationToken cancellationToken = default);
}
