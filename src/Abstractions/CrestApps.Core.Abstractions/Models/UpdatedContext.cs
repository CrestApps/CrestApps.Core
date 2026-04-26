namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.UpdatedAsync"/> after
/// a catalog entry has been successfully saved with its new values.
/// </summary>
public sealed class UpdatedContext<T> : HandlerContextBase<T>
{
    public UpdatedContext(T model)
    : base(model)
    {
    }
}
