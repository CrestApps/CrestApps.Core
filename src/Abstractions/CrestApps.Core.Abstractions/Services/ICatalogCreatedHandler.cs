using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Handler invoked after a catalog entry has been created.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogCreatedHandler<T> where T : class
{
    /// <summary>
    /// Called after a catalog entry has been created.
    /// </summary>
    /// <param name="context">The context containing the created entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task CreatedAsync(CreatedContext<T> context, CancellationToken cancellationToken = default);
}
