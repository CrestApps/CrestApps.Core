using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Handler invoked when a catalog entry is being updated.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogUpdatingHandler<T> where T : class
{
    /// <summary>
    /// Called when a catalog entry is about to be updated.
    /// </summary>
    /// <param name="context">The context containing the entry to update.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task UpdatingAsync(UpdatingContext<T> context, CancellationToken cancellationToken = default);
}
