using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// Contributes code-defined AI profiles that are not persisted in the profile store.
/// Provided profiles are merged into <see cref="IAIProfileManager.GetAsync(AIProfileType, System.Threading.CancellationToken)"/>
/// results for the requested profile type. Stored profiles with the same name take precedence.
/// </summary>
public interface IAIProfileProvider
{
    /// <summary>
    /// Gets the profiles contributed by this provider for the requested profile type.
    /// </summary>
    /// <param name="type">The profile type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read-only list of code-defined profiles.</returns>
    ValueTask<IReadOnlyList<AIProfile>> GetProfilesAsync(
        AIProfileType type,
        CancellationToken cancellationToken = default);
}
