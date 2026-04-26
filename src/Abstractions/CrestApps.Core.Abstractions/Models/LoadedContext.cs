namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.LoadedAsync"/> after
/// a catalog entry has been retrieved from the store and is ready for use.
/// </summary>
public sealed class LoadedContext<T> : HandlerContextBase<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LoadedContext"/> class.
    /// </summary>
    /// <param name="model">The model.</param>
    public LoadedContext(T model)
    : base(model)
    {
    }
}
