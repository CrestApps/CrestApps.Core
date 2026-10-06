using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Identifies the deployment a metered client talks to, as it is written onto each usage record.
/// </summary>
/// <param name="ClientName">The client (provider) name.</param>
/// <param name="ConnectionName">The connection name, or <see langword="null"/> for a contained connection.</param>
/// <param name="DeploymentName">The provider-side deployment or model name the requests are sent to.</param>
/// <param name="ModelName">The model name to record when a response does not report one.</param>
internal sealed record AIDeploymentUsageInfo(
    string ClientName,
    string ConnectionName,
    string DeploymentName,
    string ModelName)
{
    /// <summary>
    /// Describes a deployment for usage metering. The provider-side name (<see cref="AIDeployment.ModelName"/>) is
    /// what the provider bills against, so it is recorded as both the deployment and the fallback model name.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public static AIDeploymentUsageInfo From(AIDeployment deployment)
    {
        return new AIDeploymentUsageInfo(
            deployment.ClientName,
            deployment.ConnectionName,
            deployment.ModelName,
            deployment.ModelName);
    }
}
