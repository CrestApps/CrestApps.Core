using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Services;

/// <summary>
/// Represents the named Catalog Manager.
/// </summary>
public class NamedCatalogManager<T> : CatalogManager<T>, INamedCatalogManager<T>
    where T : CatalogItem, INameAwareModel, new()
{
    protected readonly INamedCatalog<T> NamedCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedCatalogManager"/> class.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="logger">The logger.</param>
    public NamedCatalogManager(
        INamedCatalog<T> catalog,
        IEnumerable<ICatalogEntryHandler<T>> handlers,
        ILogger<NamedCatalogManager<T>> logger)
    : base(catalog, handlers, logger)
    {
        NamedCatalog = catalog;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedCatalogManager"/> class.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="logger">The logger.</param>
    protected NamedCatalogManager(
        INamedCatalog<T> catalog,
        IEnumerable<ICatalogEntryHandler<T>> handlers,
        ILogger logger)
    : base(catalog, handlers, logger)
    {
        NamedCatalog = catalog;
    }

    /// <summary>
    /// Finds by name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<T> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var entry = await NamedCatalog.FindByNameAsync(name, cancellationToken);

        if (entry is not null)
        {
            await LoadAsync(entry, cancellationToken);
        }

        return entry!;
    }
}
