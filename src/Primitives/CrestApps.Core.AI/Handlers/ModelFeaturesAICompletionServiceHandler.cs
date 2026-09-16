using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Enforces the trained features declared by the resolved deployment so that request options which
/// depend on an unsupported capability are never sent to a provider. Declaring capability metadata is
/// what opts a deployment in: a deployment that declares none is left fully unconstrained.
/// </summary>
/// <remarks>
/// Calling that arrangement "opt-in" is true only of the metadata, never of the deployment. A
/// deployment does not choose whether to be constrained; whatever wrote its metadata chose for it, and
/// a source that always writes metadata makes enforcement mandatory for everything it produces. The
/// configuration deployment catalog is exactly such a source, and its records are read-only in the
/// admin UI, so an operator can neither opt out of enforcement nor widen the declaration it is held to.
/// A synthesized record that declares only <see cref="AIDeploymentFeatureNames.TextGeneration"/>
/// therefore loses every tool on every request and has its streaming downgraded — a silent, total loss
/// of tool calling that reads as a merely unhelpful model. Treat a narrow declaration from such a
/// source as a defect in the source, not as consent.
/// </remarks>
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

        // Declared metadata is the gate: a deployment without any is unconstrained so that existing
        // configurations keep working exactly as before. Past this line the declaration is binding and
        // complete, and whatever it omits is removed from the request — including from a deployment
        // whose metadata was synthesized rather than authored, which no operator can edit.
        if (context.Deployment is null || !context.Deployment.TryGet<AIDeploymentMetadata>(out _))
        {
            return Task.CompletedTask;
        }

        var capabilities = _capabilityService.GetCapabilities(context.Deployment);

        ModelFeatureEnforcement.Enforce(context.ChatOptions, capabilities, context.DeploymentName, _logger);

        return Task.CompletedTask;
    }
}
