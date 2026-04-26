namespace CrestApps.Core.Models;

/// <summary>
/// Base class for handler context objects passed to <see cref="Services.ICatalogEntryHandler{T}"/>
/// lifecycle callbacks. Carries the entry model associated with the current operation.
/// </summary>
/// <typeparam name="T">The type of the catalog entry being processed.</typeparam>
public abstract class HandlerContextBase<T>
{
    /// <summary>
    /// Gets the catalog entry associated with the current lifecycle operation.
    /// </summary>
    public T Model { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerContextBase"/> class.
    /// </summary>
    /// <param name="model">The model.</param>
    public HandlerContextBase(T model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Model = model;
    }
}
