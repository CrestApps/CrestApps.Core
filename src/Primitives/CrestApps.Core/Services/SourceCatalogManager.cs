using System.Text.Json.Nodes;
using CrestApps.Core.Extensions;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Services;

/// <summary>
/// Represents the source Catalog Manager.
/// </summary>
public class SourceCatalogManager<T> : CatalogManager<T>, ISourceCatalogManager<T>
    where T : CatalogItem, ISourceAwareModel, new()
{
    protected readonly ISourceCatalog<T> SourceCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceCatalogManager"/> class.
    /// </summary>
    /// <param name="sourceCatalog">The source catalog.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="logger">The logger.</param>
    public SourceCatalogManager(
        ISourceCatalog<T> sourceCatalog,
        IEnumerable<ICatalogEntryHandler<T>> handlers,
        ILogger<SourceCatalogManager<T>> logger)
        : base(sourceCatalog, handlers, logger)
    {
        SourceCatalog = sourceCatalog;
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
    /// Gets the operation.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<T>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        var entries = await SourceCatalog.GetAsync(source, cancellationToken);

        foreach (var entry in entries)
        {
            await LoadAsync(entry, cancellationToken);
        }

        return entries;
    }

    /// <summary>
    /// News the operation.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="data">The data.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<T> NewAsync(string source, JsonNode? data = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        var id = UniqueId.GenerateId();

        var entry = new T()
        {
            ItemId = id,
            Source = source,
        };

        var initializingContext = new InitializingContext<T>(entry, data);
        await Handlers.InvokeAsync((handler, ctx) => handler.InitializingAsync(ctx, cancellationToken), initializingContext, Logger);

        var initializedContext = new InitializedContext<T>(entry);
        await Handlers.InvokeAsync((handler, ctx) => handler.InitializedAsync(ctx, cancellationToken), initializedContext, Logger);

        if (string.IsNullOrEmpty(entry.ItemId))
        {
            entry.ItemId = id;
        }

        entry.Source = source;

        return entry;
    }

    /// <summary>
    /// News the operation.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override ValueTask<T> NewAsync(JsonNode? data = null, CancellationToken cancellationToken = default)
    {
        var source = data?["Source"]?.GetValue<string>();

        if (string.IsNullOrEmpty(source))
        {
            throw new InvalidOperationException("Data must contain a Source entry");
        }

        return NewAsync(source, data, cancellationToken);
    }
}
