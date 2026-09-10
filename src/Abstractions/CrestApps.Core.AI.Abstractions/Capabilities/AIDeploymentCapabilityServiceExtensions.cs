using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Capabilities;

/// <summary>
/// Convenience methods over <see cref="IAIDeploymentCapabilityService"/>.
/// </summary>
public static class AIDeploymentCapabilityServiceExtensions
{
    /// <summary>
    /// Determines whether the named deployment is a realtime (speech-to-speech) deployment.
    /// </summary>
    /// <param name="capabilityService">The capability service.</param>
    /// <param name="deploymentName">The deployment name to test.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> only when a deployment name is supplied and that deployment declares
    /// <see cref="AIDeploymentFeatureNames.Realtime"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the single question that decides whether a profile or interaction is a voice conversation.
    /// It replaced a stored chat mode, which was a second answer to the same question and could disagree
    /// with the deployment actually selected.
    /// </para>
    /// <para>
    /// The empty-name guard matters:
    /// <see cref="IAIDeploymentCapabilityService.ResolveDeploymentWithFeatureAsync"/> falls back to the
    /// first realtime-capable deployment when given no name, which would turn every profile that has not
    /// chosen a deployment into a voice profile.
    /// </para>
    /// </remarks>
    public static async ValueTask<bool> IsRealtimeDeploymentAsync(
        this IAIDeploymentCapabilityService capabilityService,
        string deploymentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityService);

        return !string.IsNullOrWhiteSpace(deploymentName)
            && await capabilityService.ResolveDeploymentWithFeatureAsync(AIDeploymentFeatureNames.Realtime, deploymentName, cancellationToken) is not null;
    }
}
