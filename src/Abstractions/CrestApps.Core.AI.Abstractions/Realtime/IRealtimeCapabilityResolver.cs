using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Determines whether a speech-to-speech realtime session can be run for a profile — i.e. whether a
/// realtime-purpose deployment can be resolved for it. Hosts combine this capability check with the
/// explicit opt-in signal (a chat profile whose chat mode is <see cref="ChatMode.Realtime"/>) to decide
/// whether a voice interaction should use the realtime client instead of the speech-to-text + chat +
/// text-to-speech path.
/// </summary>
public interface IRealtimeCapabilityResolver
{
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
}
