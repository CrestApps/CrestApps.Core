using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// Entity Framework Core-backed store for AI profiles.
/// </summary>
public sealed class EntityCoreAIProfileStore : NamedSourceDocumentCatalog<AIProfile>, IAIProfileStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIProfileStore"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public EntityCoreAIProfileStore(CrestAppsEntityDbContext dbContext)
        : base(dbContext)
    {
    }

    /// <summary>
    /// Gets AI profiles by profile type.
    /// </summary>
    /// <param name="type">The profile type.</param>
    public async ValueTask<IReadOnlyCollection<AIProfile>> GetByTypeAsync(AIProfileType type)
    {
        var profileType = type.ToString();
        var records = await GetReadQuery()
            .Where(x => x.Type == profileType)
            .ToListAsync();

        return records.Select(CatalogRecordFactory.Materialize<AIProfile>).ToArray();
    }
}
