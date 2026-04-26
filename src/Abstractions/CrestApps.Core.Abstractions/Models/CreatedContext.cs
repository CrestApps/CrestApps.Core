namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.CreatedAsync"/> after
/// a catalog entry has been successfully persisted for the first time.
/// </summary>
public sealed class CreatedContext<T> : HandlerContextBase<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreatedContext"/> class.
    /// </summary>
    /// <param name="model">The model.</param>
    public CreatedContext(T model)
    : base(model)
    {
    }
}
