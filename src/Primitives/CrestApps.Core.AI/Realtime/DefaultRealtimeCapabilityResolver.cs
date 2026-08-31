using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default <see cref="IRealtimeCapabilityResolver"/>: realtime is available for a profile when a
/// deployment whose purpose includes <see cref="AIDeploymentPurpose.Realtime"/> can be resolved (the
/// profile's <see cref="AIProfile.RealtimeDeploymentName"/>, or the configured default realtime deployment).
/// </summary>
public sealed class DefaultRealtimeCapabilityResolver : IRealtimeCapabilityResolver
{
    private readonly IAIDeploymentManager _deploymentManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultRealtimeCapabilityResolver"/> class.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    public DefaultRealtimeCapabilityResolver(IAIDeploymentManager deploymentManager)
    {
        _deploymentManager = deploymentManager;
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
        var deployment = await _deploymentManager.ResolveOrDefaultAsync(
            AIDeploymentPurpose.Realtime,
            realtimeDeploymentName,
            cancellationToken: cancellationToken);

        return deployment is not null && !string.IsNullOrEmpty(deployment.ModelName);
    }
}
