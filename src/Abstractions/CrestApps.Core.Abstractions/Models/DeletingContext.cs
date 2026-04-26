namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.DeletingAsync"/> just before
/// a catalog entry is removed from the store.
/// </summary>
public sealed class DeletingContext<T> : HandlerContextBase<T>
{
    public DeletingContext(T model)
    : base(model)
    {
    }
}
