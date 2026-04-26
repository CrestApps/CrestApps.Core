using System.Text.Json.Nodes;
using CrestApps.Core.Extensions;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Services;

/// <summary>
/// Represents the catalog Manager.
/// </summary>
public class CatalogManager<T> : ICatalogManager<T>
    where T : CatalogItem, new()
{
    protected readonly ICatalog<T> Catalog;
    protected readonly ILogger Logger;
    protected readonly IEnumerable<ICatalogEntryHandler<T>> Handlers;

    public CatalogManager(
        ICatalog<T> catalog,
        IEnumerable<ICatalogEntryHandler<T>> handlers,
        ILogger<CatalogManager<T>> logger)
    {
        Catalog = catalog;
        Handlers = handlers;
        Logger = logger;
    }

    protected CatalogManager(
        ICatalog<T> store,
        IEnumerable<ICatalogEntryHandler<T>> handlers,
        ILogger logger)
    {
        Catalog = store;
        Handlers = handlers;
        Logger = logger;
    }

    public async ValueTask<bool> DeleteAsync(T entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var deletingContext = new DeletingContext<T>(entry);
        await Handlers.InvokeAsync((handler, ctx) => handler.DeletingAsync(ctx, cancellationToken), deletingContext, Logger);

        if (string.IsNullOrEmpty(entry.ItemId))
        {
            return false;
        }

        var removed = await Catalog.DeleteAsync(entry, cancellationToken);

        await DeletedAsync(entry);

        var deletedContext = new DeletedContext<T>(entry);
        await Handlers.InvokeAsync((handler, ctx) => handler.DeletedAsync(ctx, cancellationToken), deletedContext, Logger);

        return removed;
    }

    public async ValueTask<T> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var entry = await Catalog.FindByIdAsync(id, cancellationToken);

        if (entry is not null)
        {
            await LoadAsync(entry, cancellationToken);

            return entry;
        }

        return null!;
    }

    public virtual async ValueTask<T> NewAsync(JsonNode? data = null, CancellationToken cancellationToken = default)
    {
        var id = UniqueId.GenerateId();

        var entry = new T()
        {
            ItemId = id,
        };

        var initializingContext = new InitializingContext<T>(entry, data);
        await Handlers.InvokeAsync((handler, ctx) => handler.InitializingAsync(ctx, cancellationToken), initializingContext, Logger);

        var initializedContext = new InitializedContext<T>(entry);
        await Handlers.InvokeAsync((handler, ctx) => handler.InitializedAsync(ctx, cancellationToken), initializedContext, Logger);

        if (string.IsNullOrEmpty(entry.ItemId))
        {
            entry.ItemId = id;
        }

        return entry;
    }

    public async ValueTask<PageResult<T>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await Catalog.PageAsync(page, pageSize, context, cancellationToken);

        foreach (var entry in result.Entries)
        {
            await LoadAsync(entry, cancellationToken);
        }

        return result;
    }

    public async ValueTask CreateAsync(T entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var creatingContext = new CreatingContext<T>(entry);
        await Handlers.InvokeAsync((handler, ctx) => handler.CreatingAsync(ctx, cancellationToken), creatingContext, Logger);

        await Catalog.CreateAsync(entry, cancellationToken);

        var createdContext = new CreatedContext<T>(entry);
        await Handlers.InvokeAsync((handler, ctx) => handler.CreatedAsync(ctx, cancellationToken), createdContext, Logger);
    }

    public async ValueTask UpdateAsync(T entry, JsonNode? data = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var updatingContext = new UpdatingContext<T>(entry, data);
        await Handlers.InvokeAsync((handler, ctx) => handler.UpdatingAsync(ctx, cancellationToken), updatingContext, Logger);

        await Catalog.UpdateAsync(entry, cancellationToken);

        var updatedContext = new UpdatedContext<T>(entry);
        await Handlers.InvokeAsync((handler, ctx) => handler.UpdatedAsync(ctx, cancellationToken), updatedContext, Logger);
    }

    public async ValueTask<ValidationResultDetails> ValidateAsync(T entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var validatingContext = new ValidatingContext<T>(entry);
        await Handlers.InvokeAsync((handler, ctx) => handler.ValidatingAsync(ctx, cancellationToken), validatingContext, Logger);

        var validatedContext = new ValidatedContext<T>(entry, validatingContext.Result);
        await Handlers.InvokeAsync((handler, ctx) => handler.ValidatedAsync(ctx, cancellationToken), validatedContext, Logger);

        return validatingContext.Result;
    }

    public async ValueTask<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var models = await Catalog.GetAllAsync(cancellationToken);

        foreach (var model in models)
        {
            await LoadAsync(model, cancellationToken);
        }

        return models;
    }

    protected virtual ValueTask DeletedAsync(T entry)
    {
        return ValueTask.CompletedTask;
    }

    protected virtual async Task LoadAsync(T entry, CancellationToken cancellationToken = default)
    {
        var loadedContext = new LoadedContext<T>(entry);

        await Handlers.InvokeAsync((handler, context) => handler.LoadedAsync(context, cancellationToken), loadedContext, Logger);
    }
}
