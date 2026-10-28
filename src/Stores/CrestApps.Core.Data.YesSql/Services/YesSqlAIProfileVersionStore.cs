using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Data.YesSql.Indexes.AI;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Services;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql implementation of <see cref="IAIProfileVersionStore"/> that keeps AI profile versions in the AI collection.
/// </summary>
public sealed class YesSqlAIProfileVersionStore : IAIProfileVersionStore
{
    private readonly ISession _session;
    private readonly string _collection;

    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAIProfileVersionStore"/> class.
    /// </summary>
    /// <param name="session">The YesSql session.</param>
    /// <param name="options">The YesSql store options that name the AI collection.</param>
    public YesSqlAIProfileVersionStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collection = options.Value.AICollectionName;
    }

    /// <summary>
    /// Saves a version.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the version is staged for saving.</returns>
    public async Task SaveAsync(AIProfileVersion version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        await _session.SaveAsync(version, false, _collection, cancellationToken);
    }

    /// <summary>
    /// Gets a version by its identifier.
    /// </summary>
    /// <param name="itemId">The version's identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The version, or <see langword="null"/> when there is none.</returns>
    public async Task<AIProfileVersion> FindByIdAsync(string itemId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            return null;
        }

        return await _session
            .Query<AIProfileVersion, AIProfileVersionIndex>(index => index.ItemId == itemId, _collection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the highest version number of a profile, or zero when it has none.
    /// </summary>
    /// <param name="profileId">The profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The highest version number.</returns>
    public async Task<int> GetLatestVersionAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(profileId))
        {
            return 0;
        }

        var latest = await _session
            .QueryIndex<AIProfileVersionIndex>(index => index.ProfileId == profileId, _collection)
            .OrderByDescending(index => index.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return latest?.Version ?? 0;
    }

    /// <summary>
    /// Gets a page of a profile's versions, newest first, and how many it has.
    /// </summary>
    /// <param name="profileId">The profile.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">The number of versions per page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The versions of the page and the profile's total number of versions.</returns>
    public async Task<(IReadOnlyList<AIProfileVersion> Versions, int Count)> PageAsync(string profileId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(profileId))
        {
            return ([], 0);
        }

        var query = _session.Query<AIProfileVersion, AIProfileVersionIndex>(index => index.ProfileId == profileId, _collection);
        var count = await query.CountAsync(cancellationToken);
        var versions = await query
            .OrderByDescending(index => index.Version)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .ListAsync(cancellationToken);

        return (versions.ToList(), count);
    }
}
