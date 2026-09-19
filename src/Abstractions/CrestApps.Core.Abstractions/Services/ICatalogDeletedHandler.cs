using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Handler invoked after a catalog entry has been deleted.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogDeletedHandler<T> where T : class
{
    /// <summary>
    /// Called after a catalog entry has been deleted.
    /// </summary>
    /// <param name="context">The context containing the deleted entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task DeletedAsync(DeletedContext<T> context, CancellationToken cancellationToken = default);
}
