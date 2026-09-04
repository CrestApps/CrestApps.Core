using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Enforces the trained features declared by the resolved deployment so that request options which
/// depend on an unsupported capability are never sent to a provider. Enforcement is opt-in: only
/// deployments that declare capability metadata are constrained, which keeps deployments without
/// declared capabilities fully unconstrained.
/// </summary>
public sealed class ModelFeaturesAICompletionServiceHandler : IAICompletionServiceHandler
{
    private readonly IAIDeploymentCapabilityService _capabilityService;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelFeaturesAICompletionServiceHandler"/> class.
    /// </summary>
    /// <param name="capabilityService">The capability service used to resolve deployment metadata.</param>
    /// <param name="logger">The logger.</param>
    public ModelFeaturesAICompletionServiceHandler(
        IAIDeploymentCapabilityService capabilityService,
        ILogger<ModelFeaturesAICompletionServiceHandler> logger)
    {
        _capabilityService = capabilityService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task ConfigureAsync(CompletionServiceConfigureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Feature enforcement is opt-in: only deployments that declare their capability metadata
        // constrain the request. Deployments without metadata are treated as unconstrained so that
        // existing configurations keep working exactly as before.
        if (context.Deployment is null || !context.Deployment.TryGet<AIDeploymentMetadata>(out _))
        {
            return Task.CompletedTask;
        }

        var capabilities = _capabilityService.GetCapabilities(context.Deployment);

        ModelFeatureEnforcement.Enforce(context.ChatOptions, capabilities, context.DeploymentName, _logger);

        return Task.CompletedTask;
    }
}
