namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.DeletedAsync"/> after
/// a catalog entry has been successfully removed from the store.
/// </summary>
public sealed class DeletedContext<T> : HandlerContextBase<T>
{
    public DeletedContext(T entry)
    : base(entry)
    {
    }
}
