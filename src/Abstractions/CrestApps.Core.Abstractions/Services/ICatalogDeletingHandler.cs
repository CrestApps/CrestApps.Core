using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Handler invoked when a catalog entry is being deleted.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogDeletingHandler<T> where T : class
{
    /// <summary>
    /// Called when a catalog entry is about to be deleted.
    /// </summary>
    /// <param name="context">The context containing the entry to delete.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task DeletingAsync(DeletingContext<T> context, CancellationToken cancellationToken = default);
}
