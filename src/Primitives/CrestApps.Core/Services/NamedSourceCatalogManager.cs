using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Services;

public class NamedSourceCatalogManager<T> : SourceCatalogManager<T>, INamedCatalogManager<T>, ISourceCatalogManager<T>, INamedSourceCatalogManager<T>
    where T : CatalogItem, INameAwareModel, ISourceAwareModel, new()
{
    protected readonly INamedSourceCatalog<T> NamedSourceCatalog;

    public NamedSourceCatalogManager(
        INamedSourceCatalog<T> catalog,
        IEnumerable<ICatalogEntryHandler<T>> handlers,
        ILogger<NamedSourceCatalogManager<T>> logger)
        : base(catalog, handlers, logger)
    {
        NamedSourceCatalog = catalog;
    }

    public async ValueTask<T> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var entry = await NamedSourceCatalog.FindByNameAsync(name, cancellationToken);

        if (entry is not null)
        {
            await LoadAsync(entry, cancellationToken);
        }

        return entry!;
    }

    public async ValueTask<T> GetAsync(string name, string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(source);

        var entry = await NamedSourceCatalog.GetAsync(name, source, cancellationToken);

        if (entry is not null)
        {
            await LoadAsync(entry, cancellationToken);
        }

        return entry!;
    }
}
