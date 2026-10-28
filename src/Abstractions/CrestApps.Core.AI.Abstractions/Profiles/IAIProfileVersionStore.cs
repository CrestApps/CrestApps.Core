using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// Keeps the saved versions of AI profiles.
/// </summary>
public interface IAIProfileVersionStore
{
    /// <summary>
    /// Saves a version.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the version is staged for saving.</returns>
    Task SaveAsync(AIProfileVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a version by its identifier.
    /// </summary>
    /// <param name="itemId">The version's identifier.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The version, or <see langword="null"/> when there is none.</returns>
    Task<AIProfileVersion> FindByIdAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the highest version number of a profile, or zero when it has none.
    /// </summary>
    /// <param name="profileId">The profile.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The highest version number.</returns>
    Task<int> GetLatestVersionAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of a profile's versions, newest first, and how many it has.
    /// </summary>
    /// <param name="profileId">The profile.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">The number of versions per page.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The versions of the page and the profile's total number of versions.</returns>
    Task<(IReadOnlyList<AIProfileVersion> Versions, int Count)> PageAsync(string profileId, int page, int pageSize, CancellationToken cancellationToken = default);
}
