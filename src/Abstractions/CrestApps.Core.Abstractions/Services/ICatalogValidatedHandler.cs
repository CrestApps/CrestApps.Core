using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Handler invoked after a catalog entry has been validated.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogValidatedHandler<T> where T : class
{
    /// <summary>
    /// Called after a catalog entry has been validated.
    /// </summary>
    /// <param name="context">The context containing the validated entry and any validation results.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task ValidatedAsync(ValidatedContext<T> context, CancellationToken cancellationToken = default);
}
