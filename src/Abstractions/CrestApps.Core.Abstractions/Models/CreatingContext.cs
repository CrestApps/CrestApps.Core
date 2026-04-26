namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.CreatingAsync"/> just before
/// a catalog entry is persisted for the first time.
/// </summary>
public sealed class CreatingContext<T> : HandlerContextBase<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreatingContext"/> class.
    /// </summary>
    /// <param name="model">The model.</param>
    public CreatingContext(T model)
    : base(model)
    {
    }
}
