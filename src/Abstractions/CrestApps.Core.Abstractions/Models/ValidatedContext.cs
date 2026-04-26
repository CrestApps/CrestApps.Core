namespace CrestApps.Core.Models;

/// <summary>
/// Context passed to <see cref="Services.ICatalogEntryHandler{T}.ValidatedAsync"/> after
/// the full validation pipeline has completed for a catalog entry.
/// </summary>
public sealed class ValidatedContext<T> : HandlerContextBase<T>
{
    /// <summary>
    /// Gets the aggregated validation result produced by all validating handlers.
    /// Inspect <see cref="ValidationResultDetails.Succeeded"/> and
    /// <see cref="ValidationResultDetails.Errors"/> to determine the outcome.
    /// </summary>
    public ValidationResultDetails Result { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidatedContext"/> class.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="result">The result.</param>
    public ValidatedContext(
        T model,
        ValidationResultDetails result)
        : base(model)
    {
        Result = result ?? new();
    }
}
