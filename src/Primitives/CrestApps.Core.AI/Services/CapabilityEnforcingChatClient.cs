using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// A <see cref="DelegatingChatClient"/> that enforces the trained features declared by a deployment
/// on every request, including requests issued directly against a client resolved from
/// <see cref="IAIClientFactory"/> outside the completion pipeline. Options that depend on an
/// undeclared feature (for example tools or a JSON response format) are removed before the request
/// reaches the provider, which prevents avoidable provider validation errors such as HTTP 400. When a
/// deployment does not declare the <c>streaming</c> feature, a streaming request is transparently
/// completed as a single buffered response instead of streaming incrementally. Enforcement is opt-in:
/// only deployments that declare capability metadata are constrained.
/// </summary>
internal sealed class CapabilityEnforcingChatClient : DelegatingChatClient
{
    private readonly AIDeployment _deployment;
    private readonly IAIDeploymentCapabilityService _capabilityService;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CapabilityEnforcingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner chat client that performs the provider request.</param>
    /// <param name="deployment">The deployment whose declared capabilities are enforced.</param>
    /// <param name="capabilityService">The capability service used to resolve the deployment capabilities.</param>
    /// <param name="logger">The logger used to report removed options.</param>
    public CapabilityEnforcingChatClient(
        IChatClient innerClient,
        AIDeployment deployment,
        IAIDeploymentCapabilityService capabilityService,
        ILogger logger)
        : base(innerClient)
    {
        _deployment = deployment;
        _capabilityService = capabilityService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(messages, Enforce(options), cancellationToken);
    }

    /// <inheritdoc/>
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var enforced = Enforce(options);

        if (IsStreamingSuppressed())
        {
            _logger.LogWarning(
                "Deployment '{Deployment}' does not declare the '{Feature}' feature. The streaming request was completed as a single non-streaming response.",
                _deployment.ModelName, AIDeploymentFeatureNames.Streaming);

            return BufferAsStreamAsync(messages, enforced, cancellationToken);
        }

        return base.GetStreamingResponseAsync(messages, enforced, cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> BufferAsStreamAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    private bool IsStreamingSuppressed()
    {
        // Streaming is a method choice rather than a request option, so it is enforced here by
        // completing the request without streaming. Only deployments that declare capability metadata
        // are constrained, which keeps existing configurations unchanged.
        if (!_deployment.TryGet<AIDeploymentModelMetadata>(out _))
        {
            return false;
        }

        return !_capabilityService.GetCapabilities(_deployment).SupportsFeature(AIDeploymentFeatureNames.Streaming);
    }

    private ChatOptions Enforce(ChatOptions options)
    {
        // Enforcement is opt-in: a deployment without declared capability metadata is left untouched
        // so existing configurations keep working exactly as before.
        if (options is null || !_deployment.TryGet<AIDeploymentModelMetadata>(out _))
        {
            return options;
        }

        var capabilities = _capabilityService.GetCapabilities(_deployment);

        // Clone before mutating so a caller that reuses the same options instance across requests is
        // never affected by the enforcement performed for this deployment.
        var enforced = options.Clone();

        return ModelFeatureEnforcement.Enforce(enforced, capabilities, _deployment.ModelName, _logger)
            ? enforced
            : options;
    }
}
