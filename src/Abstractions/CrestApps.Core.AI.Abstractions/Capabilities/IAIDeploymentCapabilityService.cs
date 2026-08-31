using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Capabilities;

/// <summary>
/// Resolves the metadata-driven capabilities of an <see cref="AIDeployment"/> by merging the globally
/// registered model features and parameters with the metadata stored on the deployment.
/// </summary>
public interface IAIDeploymentCapabilityService
{
    /// <summary>
    /// Gets every model feature registered by the application.
    /// </summary>
    IReadOnlyList<AIDeploymentFeatureDescriptor> GetRegisteredFeatures();

    /// <summary>
    /// Gets every model parameter registered by the application.
    /// </summary>
    IReadOnlyList<AIDeploymentParameterDescriptor> GetRegisteredParameters();

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

    /// <summary>
    /// Gets every deployment whose model declares the given feature.
    /// </summary>
    /// <param name="featureName">The technical name of the required feature.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask<IReadOnlyList<AIDeployment>> GetDeploymentsWithFeatureAsync(string featureName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a deployment whose model declares the given feature. When <paramref name="deploymentName"/>
    /// is provided, that deployment is returned only if it declares the feature; otherwise the first
    /// deployment that declares the feature is returned. Returns <see langword="null"/> when none qualifies.
    /// </summary>
    /// <param name="featureName">The technical name of the required feature.</param>
    /// <param name="deploymentName">An explicit deployment name, or <see langword="null"/> to use the first qualifying deployment.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask<AIDeployment> ResolveDeploymentWithFeatureAsync(string featureName, string deploymentName = null, CancellationToken cancellationToken = default);
}
