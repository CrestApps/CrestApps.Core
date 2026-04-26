using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public class NamedDocumentCatalog<T> : DocumentCatalog<T>, INamedCatalog<T>
    where T : CatalogItem, INameAwareModel
{
    public NamedDocumentCatalog(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<T>> logger = null)
        : base(dbContext, logger)
    {
    }

    public async ValueTask<T> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var record = await GetReadQuery()
            .FirstOrDefaultAsync(x => x.Name == name, cancellationToken);

        return record is null ? null : CatalogRecordFactory.Materialize<T>(record);
    }

    protected override async ValueTask SavingAsync(T record)
    {
        var exists = await GetReadQuery()
            .AnyAsync(x => x.Name == record.Name && x.ItemId != record.ItemId);

        if (exists)
        {
            throw new InvalidOperationException("There is already another model with the same name.");
        }
    }
}
