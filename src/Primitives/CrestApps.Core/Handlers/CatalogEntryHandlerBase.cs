using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Handlers;
public abstract class CatalogEntryHandlerBase<T> : ICatalogEntryHandler<T>
{
    public virtual Task DeletedAsync(DeletedContext<T> context)
    {
        return Task.CompletedTask;
    }

    public virtual Task DeletingAsync(DeletingContext<T> context)
    {
        return Task.CompletedTask;
    }

    public virtual Task InitializedAsync(InitializedContext<T> context)
    {
        return Task.CompletedTask;
    }

    public virtual Task InitializingAsync(InitializingContext<T> context)
    {
        return Task.CompletedTask;
    }

    public virtual Task LoadedAsync(LoadedContext<T> context)
    {
        return Task.CompletedTask;
    }

    public virtual Task CreatedAsync(CreatedContext<T> context)
    {
        return Task.CompletedTask;
    }

    public virtual Task CreatingAsync(CreatingContext<T> context)
    {
        return Task.CompletedTask;
    }

    public virtual Task UpdatedAsync(UpdatedContext<T> context)
    {
        return Task.CompletedTask;
    }

    public virtual Task UpdatingAsync(UpdatingContext<T> context)
    {
        return Task.CompletedTask;
    }

    public virtual Task ValidatedAsync(ValidatedContext<T> context)
    {
        return Task.CompletedTask;
    }

    public virtual Task ValidatingAsync(ValidatingContext<T> context)
    {
        return Task.CompletedTask;
    }
}