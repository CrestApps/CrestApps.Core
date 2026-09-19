using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Handler invoked when a catalog entry is being created.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogCreatingHandler<T> where T : class
{
    /// <summary>
    /// Called when a catalog entry is about to be created.
    /// </summary>
    /// <param name="context">The context containing the entry to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task CreatingAsync(CreatingContext<T> context, CancellationToken cancellationToken = default);
}
