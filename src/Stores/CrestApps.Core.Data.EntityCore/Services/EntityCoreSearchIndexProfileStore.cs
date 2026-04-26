using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreSearchIndexProfileStore : NamedDocumentCatalog<SearchIndexProfile>, ISearchIndexProfileStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreSearchIndexProfileStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    public EntityCoreSearchIndexProfileStore(CrestAppsEntityDbContext dbContext)
        : base(dbContext)
    {
    }

    /// <summary>
    /// Gets by type.
    /// </summary>
    /// <param name="type">The type.</param>
    public async Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        var records = await GetReadQuery().Where(x => x.Type == type).ToListAsync();

return records.Select(CatalogRecordFactory.Materialize<SearchIndexProfile>).ToArray();
    }
}
