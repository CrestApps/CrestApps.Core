using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Services;

public class NamedCatalogManager<T> : CatalogManager<T>, INamedCatalogManager<T>
    where T : CatalogItem, INameAwareModel, new()
{
    protected readonly INamedCatalog<T> NamedCatalog;

    public NamedCatalogManager(
        INamedCatalog<T> catalog,
        IEnumerable<ICatalogEntryHandler<T>> handlers,
        ILogger<NamedCatalogManager<T>> logger)
    : base(catalog, handlers, logger)
    {
        NamedCatalog = catalog;
    }

    protected NamedCatalogManager(
        INamedCatalog<T> catalog,
        IEnumerable<ICatalogEntryHandler<T>> handlers,
        ILogger logger)
    : base(catalog, handlers, logger)
    {
        NamedCatalog = catalog;
    }

    public async ValueTask<T> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var entry = await NamedCatalog.FindByNameAsync(name, cancellationToken);

        if (entry is not null)
        {
            await LoadAsync(entry, cancellationToken);
        }

        return entry;
    }
}
