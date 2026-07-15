using System.Text.Json.Nodes;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Services;

/// <summary>
/// Represents the named Source Catalog Manager.
/// </summary>
public class NamedSourceCatalogManager<T> : CatalogManagerBase<T>, INamedCatalogManager<T>, ISourceCatalogManager<T>, INamedSourceCatalogManager<T>
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

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<T>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        var entries = await NamedSourceCatalog.GetAsync(source, cancellationToken);

        foreach (var entry in entries)
        {
            await LoadAsync(entry, cancellationToken);
        }

        return entries;
    }

    /// <summary>
    /// Finds by source.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<T>> FindBySourceAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        var entries = (await Catalog.GetAllAsync(cancellationToken)).Where(x => x.Source == source);

        foreach (var entry in entries)
        {
            await LoadAsync(entry, cancellationToken);
        }

        return entries;
    }

    /// <summary>
    /// News the operation.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="source">The source.</param>
    /// <param name="data">The data.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual async ValueTask<T> NewAsync(string name, string source, JsonNode? data = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(source);

        var entry = new T
        {
            Source = source,
        };

        SetName(entry, name);

        entry = await InitializeNewEntryAsync(entry, data, cancellationToken);
        entry.Source = source;
        SetName(entry, name);

        return entry;
    }

    ValueTask<T> INamedCatalogManager<T>.NewAsync(string name, JsonNode data, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var entry = new T();
        SetName(entry, name);

        return InitializeNameOnlyEntryAsync(entry, name, data, cancellationToken);
    }

    ValueTask<T> ISourceCatalogManager<T>.NewAsync(string source, JsonNode data, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return InitializeSourceOnlyEntryAsync(source, data, cancellationToken);
    }

    private async ValueTask<T> InitializeNameOnlyEntryAsync(T entry, string name, JsonNode? data, CancellationToken cancellationToken)
    {
        entry = await InitializeNewEntryAsync(entry, data, cancellationToken);
        SetName(entry, name);

        return entry;
    }

    private async ValueTask<T> InitializeSourceOnlyEntryAsync(string source, JsonNode? data, CancellationToken cancellationToken)
    {
        var entry = new T
        {
            Source = source,
        };

        entry = await InitializeNewEntryAsync(entry, data, cancellationToken);
        entry.Source = source;

        return entry;
    }
}
