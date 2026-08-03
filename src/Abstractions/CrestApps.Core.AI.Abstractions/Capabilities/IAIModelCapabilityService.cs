using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Capabilities;

/// <summary>
/// Resolves the metadata-driven capabilities of an <see cref="AIDeployment"/> by merging the globally
/// registered model features and parameters with the metadata stored on the deployment.
/// </summary>
public interface IAIModelCapabilityService
{
    /// <summary>
    /// Gets every model feature registered by the application.
    /// </summary>
    IReadOnlyList<AIModelFeatureDescriptor> GetRegisteredFeatures();

    /// <summary>
    /// Gets every model parameter registered by the application.
    /// </summary>
    IReadOnlyList<AIModelParameterDescriptor> GetRegisteredParameters();

    /// <summary>
    /// Gets the effective capabilities exposed by the given deployment.
    /// </summary>
    /// <param name="deployment">The deployment to inspect.</param>
    AIDeploymentCapabilities GetCapabilities(AIDeployment deployment);

    /// <summary>
    /// Gets the effective capabilities exposed by the deployment with the given technical name.
    /// </summary>
    /// <param name="deploymentName">The technical name of the deployment.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask<AIDeploymentCapabilities> GetCapabilitiesAsync(string deploymentName, CancellationToken cancellationToken = default);
}
