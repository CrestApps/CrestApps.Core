namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.ValidatingAsync"/> while
/// a catalog entry is being validated. Handlers may add errors to
/// <see cref="Result"/> to indicate failure.
/// </summary>
public sealed class ValidatingContext<T> : HandlerContextBase<T>
{
    /// <summary>
    /// Gets the mutable validation result that handlers populate during the validating phase.
    /// Call <see cref="ValidationResultDetails.Fail"/> to record an error and mark the result as failed.
    /// </summary>
    public ValidationResultDetails Result { get; } = new();

    public ValidatingContext(T model)
    : base(model)
    {
    }
}
