using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default <see cref="IRealtimeCapabilityResolver"/>. Realtime is a model capability rather than a
/// deployment purpose: a realtime deployment is a <see cref="AIDeploymentPurpose.Chat"/> deployment whose
/// model declares the <see cref="AIModelFeatureNames.Realtime"/> capability. Resolution prefers an
/// explicit deployment name, then the site-configured default realtime deployment, then the first
/// realtime-capable chat deployment, and only returns a deployment that actually declares the capability.
/// </summary>
public sealed class DefaultRealtimeCapabilityResolver : IRealtimeCapabilityResolver
{
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIModelCapabilityService _capabilityService;
    private readonly IOptionsMonitor<DefaultAIDeploymentSettings> _deploymentSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultRealtimeCapabilityResolver"/> class.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="capabilityService">The model capability service.</param>
    /// <param name="deploymentSettings">The default deployment settings.</param>
    public DefaultRealtimeCapabilityResolver(
        IAIDeploymentManager deploymentManager,
        IAIModelCapabilityService capabilityService,
        IOptionsMonitor<DefaultAIDeploymentSettings> deploymentSettings)
    {
        _deploymentManager = deploymentManager;
        _capabilityService = capabilityService;
        _deploymentSettings = deploymentSettings;
    }

    /// <inheritdoc />
    public async ValueTask<AIDeployment> ResolveRealtimeDeploymentAsync(string realtimeDeploymentName = null, CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(realtimeDeploymentName)
            ? _deploymentSettings.CurrentValue.DefaultRealtimeDeploymentName
            : realtimeDeploymentName;

        AIDeployment deployment = null;

        if (!string.IsNullOrWhiteSpace(name))
        {
            deployment = await _deploymentManager.FindByNameAsync(name, cancellationToken);
        }

        if (deployment is null)
        {
            // Fall back to the first chat deployment that declares the realtime capability.
            var realtimeDeployments = await GetRealtimeDeploymentsAsync(cancellationToken);

            deployment = realtimeDeployments.Count > 0 ? realtimeDeployments[0] : null;
        }

        if (deployment is null || string.IsNullOrEmpty(deployment.ModelName) || !IsRealtimeCapable(deployment))
        {
            return null;
        }

        return deployment;
    }

    /// <inheritdoc />
    public ValueTask<bool> IsRealtimeAvailableAsync(AIProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return IsRealtimeDeploymentAvailableAsync(profile.RealtimeDeploymentName, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsRealtimeDeploymentAvailableAsync(string realtimeDeploymentName = null, CancellationToken cancellationToken = default)
    {
        var deployment = await ResolveRealtimeDeploymentAsync(realtimeDeploymentName, cancellationToken);

        return deployment is not null;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AIDeployment>> GetRealtimeDeploymentsAsync(CancellationToken cancellationToken = default)
    {
        var chatDeployments = await _deploymentManager.GetByPurposeAsync(AIDeploymentPurpose.Chat, cancellationToken);

        return [.. chatDeployments.Where(IsRealtimeCapable)];
    }

    private bool IsRealtimeCapable(AIDeployment deployment)
        => _capabilityService.GetCapabilities(deployment).SupportsFeature(AIModelFeatureNames.Realtime);
}
