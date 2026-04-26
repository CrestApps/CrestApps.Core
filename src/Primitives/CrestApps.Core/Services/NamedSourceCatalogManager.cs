using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Services;

/// <summary>
/// Represents the named Source Catalog Manager.
/// </summary>
public class NamedSourceCatalogManager<T> : SourceCatalogManager<T>, INamedCatalogManager<T>, ISourceCatalogManager<T>, INamedSourceCatalogManager<T>
    where T : CatalogItem, INameAwareModel, ISourceAwareModel, new()
{
    protected readonly INamedSourceCatalog<T> NamedSourceCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedSourceCatalogManager"/> class.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="logger">The logger.</param>
    public NamedSourceCatalogManager(
        INamedSourceCatalog<T> catalog,
        IEnumerable<ICatalogEntryHandler<T>> handlers,
        ILogger<NamedSourceCatalogManager<T>> logger)
        : base(catalog, handlers, logger)
    {
        NamedSourceCatalog = catalog;
    }

    /// <summary>
    /// Finds by name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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
