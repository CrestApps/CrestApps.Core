namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.InitializedAsync"/> after
/// a catalog entry has been fully initialized with its default values.
/// </summary>
public sealed class InitializedContext<T> : HandlerContextBase<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InitializedContext"/> class.
    /// </summary>
    /// <param name="model">The model.</param>
    public InitializedContext(T model)
    : base(model)
    {
    }
}
