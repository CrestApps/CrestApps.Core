using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Resolves the deployment used for a speech-to-speech realtime session. A realtime deployment is an
/// ordinary <see cref="AIDeploymentPurpose.Chat"/> deployment whose model declares the
/// <see cref="AIModelFeatureNames.Realtime"/> capability; there is no dedicated realtime deployment
/// purpose. Hosts combine the availability check with the explicit opt-in signal (a chat profile whose
/// chat mode is <see cref="ChatMode.Realtime"/>) to decide whether a voice interaction should use the
/// realtime client instead of the speech-to-text + chat + text-to-speech path.
/// </summary>
public interface IRealtimeCapabilityResolver
{
    /// <summary>
    /// Resolves the realtime-capable deployment to use, or <see langword="null"/> when none is available.
    /// Resolution uses the given deployment name (or the site-configured default realtime deployment, then
    /// the first realtime-capable chat deployment) and only returns a deployment whose model declares the
    /// <see cref="AIModelFeatureNames.Realtime"/> capability.
    /// </summary>
    /// <param name="realtimeDeploymentName">An explicit deployment name, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask<AIDeployment> ResolveRealtimeDeploymentAsync(string realtimeDeploymentName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a realtime deployment is available for the given profile.
    /// </summary>
    /// <param name="profile">The profile to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask<bool> IsRealtimeAvailableAsync(AIProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a realtime deployment is available, using the given deployment name or the
    /// site-configured default realtime deployment. Used by callers that have no profile (e.g. chat
    /// interactions).
    /// </summary>
    /// <param name="realtimeDeploymentName">An explicit realtime deployment name, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask<bool> IsRealtimeDeploymentAvailableAsync(string realtimeDeploymentName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every chat deployment whose model declares the <see cref="AIModelFeatureNames.Realtime"/>
    /// capability. Suitable for populating realtime deployment selectors.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask<IReadOnlyList<AIDeployment>> GetRealtimeDeploymentsAsync(CancellationToken cancellationToken = default);
}
