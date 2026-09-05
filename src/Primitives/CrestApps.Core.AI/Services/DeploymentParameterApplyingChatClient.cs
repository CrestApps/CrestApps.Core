using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// A <see cref="DelegatingChatClient"/> that applies the model parameter values an operator selected
/// for a deployment to every request issued through it. Callers that resolve a chat client directly
/// from <see cref="Clients.IAIClientFactory"/> — such as the background utility completions used for
/// planning, data extraction, and post-session processing — bypass the completion pipeline, so this
/// client gives them the same parameter handling the pipeline provides.
/// </summary>
internal sealed class DeploymentParameterApplyingChatClient : DelegatingChatClient
{
    private readonly IAIDeploymentParameterApplier _applier;
    private readonly AIDeployment _deployment;
    private readonly AICompletionContext _completionContext;
    private readonly AIDeploymentParameterScope _scope;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeploymentParameterApplyingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner chat client that performs the provider request.</param>
    /// <param name="applier">The applier that binds the selected values onto the chat options.</param>
    /// <param name="deployment">The deployment resolved for the request.</param>
    /// <param name="completionContext">The completion context holding the selected values.</param>
    /// <param name="scope">The set of selected values to apply.</param>
    public DeploymentParameterApplyingChatClient(
        IChatClient innerClient,
        IAIDeploymentParameterApplier applier,
        AIDeployment deployment,
        AICompletionContext completionContext,
        AIDeploymentParameterScope scope)
        : base(innerClient)
    {
        _applier = applier;
        _deployment = deployment;
        _completionContext = completionContext;
        _scope = scope;
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return await base.GetResponseAsync(messages, await ApplyAsync(options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var applied = await ApplyAsync(options, cancellationToken);

        await foreach (var update in base.GetStreamingResponseAsync(messages, applied, cancellationToken))
        {
            yield return update;
        }
    }

    private async Task<ChatOptions> ApplyAsync(ChatOptions options, CancellationToken cancellationToken)
    {
        if (_applier is null || _completionContext is null)
        {
            return options;
        }

        // Clone before mutating so a caller that reuses the same options instance across requests is
        // never affected by the values applied for this deployment.
        var applied = options?.Clone() ?? new ChatOptions();

        await _applier.ApplyAsync(applied, _deployment, _completionContext, _scope, cancellationToken);

        return applied;
    }
}
