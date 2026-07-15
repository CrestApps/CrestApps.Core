using System.Text.Json.Nodes;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Services;

public class CatalogManager<T> : CatalogManagerBase<T>, ICatalogManager<T>
    where T : CatalogItem, new()
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogManager"/> class.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="logger">The logger.</param>
    public CatalogManager(
        ICatalog<T> catalog,
        IEnumerable<ICatalogEntryHandler<T>> handlers,
        ILogger<CatalogManager<T>> logger)
        : base(catalog, handlers, logger)
    {
    }

    /// <summary>
    /// News the operation.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual async ValueTask<T> NewAsync(JsonNode? data = null, CancellationToken cancellationToken = default)
    {
        return await InitializeNewEntryAsync(new T(), data, cancellationToken);
    }
}
