using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Handlers;

/// <summary>
/// Represents the catalog Entry Handler Base.
/// </summary>
public abstract class CatalogEntryHandlerBase<T> : ICatalogEntryHandler<T>
{
    public virtual Task DeletedAsync(DeletedContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task DeletingAsync(DeletingContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task InitializedAsync(InitializedContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task InitializingAsync(InitializingContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task LoadedAsync(LoadedContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task CreatedAsync(CreatedContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task CreatingAsync(CreatingContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task UpdatedAsync(UpdatedContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task UpdatingAsync(UpdatingContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task ValidatedAsync(ValidatedContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task ValidatingAsync(ValidatingContext<T> context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
