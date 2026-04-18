using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// Commits all tracked Entity Framework Core changes for the current request or scope.
/// Registered automatically by <c>AddEntityCoreDataStore</c>.
/// </summary>
public sealed class EntityCoreStoreCommitter : IStoreCommitter
{
    private readonly CrestAppsEntityDbContext _dbContext;
    private readonly ILogger<EntityCoreStoreCommitter> _logger;

    public EntityCoreStoreCommitter(CrestAppsEntityDbContext dbContext, ILogger<EntityCoreStoreCommitter> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        if (!_dbContext.ChangeTracker.HasChanges())
        {
            return;
        }

        _logger.LogDebug("EntityCoreStoreCommitter flushing tracked Entity Framework Core changes.");
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
