namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.UpdatedAsync"/> after
/// a catalog entry has been successfully saved with its new values.
/// </summary>
public sealed class UpdatedContext<T> : HandlerContextBase<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatedContext"/> class.
    /// </summary>
    /// <param name="model">The model.</param>
    public UpdatedContext(T model)
    : base(model)
    {
    }
}
