using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
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
    private readonly IAIModelCapabilityService _capabilityService;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelFeaturesAICompletionServiceHandler"/> class.
    /// </summary>
    /// <param name="capabilityService">The capability service used to resolve deployment metadata.</param>
    /// <param name="logger">The logger.</param>
    public ModelFeaturesAICompletionServiceHandler(
        IAIModelCapabilityService capabilityService,
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
        if (context.Deployment is null || !context.Deployment.TryGet<AIDeploymentModelMetadata>(out _))
        {
            return Task.CompletedTask;
        }

        var capabilities = _capabilityService.GetCapabilities(context.Deployment);

        if (!capabilities.SupportsFeature(AIModelFeatureNames.ToolCalling) && context.ChatOptions.Tools is { Count: > 0 })
        {
            _logger.LogWarning(
                "Deployment '{Deployment}' does not declare the '{Feature}' feature. {Count} tool(s) were removed from the request.",
                context.DeploymentName, AIModelFeatureNames.ToolCalling, context.ChatOptions.Tools.Count);

            context.ChatOptions.Tools = null;
            context.ChatOptions.ToolMode = null;
        }

        if (!capabilities.SupportsFeature(AIModelFeatureNames.StructuredOutputs) && context.ChatOptions.ResponseFormat is ChatResponseFormatJson)
        {
            _logger.LogWarning(
                "Deployment '{Deployment}' does not declare the '{Feature}' feature. The JSON response format was removed from the request.",
                context.DeploymentName, AIModelFeatureNames.StructuredOutputs);

            context.ChatOptions.ResponseFormat = null;
        }

        return Task.CompletedTask;
    }
}
