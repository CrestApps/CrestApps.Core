using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// EntityCore implementation of <see cref="IAIProfileVersionStore"/>. A version's profile is kept in the record's
/// reference column and its save time in the creation column, which orders a profile's history.
/// </summary>
public sealed class EntityCoreAIProfileVersionStore : DocumentCatalog<AIProfileVersion>, IAIProfileVersionStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIProfileVersionStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreAIProfileVersionStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<AIProfileVersion>> logger = null)
        : base(dbContext, logger)
    {
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

        await UpdateAsync(version, cancellationToken);
    }

    /// <summary>
    /// Gets a version by its identifier.
    /// </summary>
    /// <param name="itemId">The version's identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The version, or <see langword="null"/> when there is none.</returns>
    async Task<AIProfileVersion> IAIProfileVersionStore.FindByIdAsync(string itemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            return null;
        }

        return await FindByIdAsync(itemId, cancellationToken);
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

        var record = await GetReadQuery()
            .Where(x => x.ReferenceId == profileId)
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return record is null
            ? 0
            : CatalogRecordFactory.Materialize<AIProfileVersion>(record).Version;
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

        var query = GetReadQuery().Where(x => x.ReferenceId == profileId);
        var count = await query.CountAsync(cancellationToken);
        var records = await query
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (records.Select(CatalogRecordFactory.Materialize<AIProfileVersion>).ToList(), count);
    }
}
