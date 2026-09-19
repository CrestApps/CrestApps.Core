using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Handler invoked when a catalog entry is being validated.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogValidatingHandler<T> where T : class
{
    /// <summary>
    /// Called when a catalog entry is about to be validated.
    /// </summary>
    /// <param name="context">The context containing the entry to validate.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task ValidatingAsync(ValidatingContext<T> context, CancellationToken cancellationToken = default);
}
