using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public class NamedDocumentCatalog<T> : DocumentCatalog<T>, INamedCatalog<T>
    where T : CatalogItem, INameAwareModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NamedDocumentCatalog"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public NamedDocumentCatalog(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<T>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <summary>
    /// Finds by name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<T> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var record = await GetReadQuery()
            .FirstOrDefaultAsync(x => x.Name == name, cancellationToken);

return record is null ? null : CatalogRecordFactory.Materialize<T>(record);
    }

    /// <summary>
    /// Savings the operation.
    /// </summary>
    /// <param name="record">The record.</param>
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
