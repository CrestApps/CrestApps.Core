using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Handler invoked after a catalog entry has been updated.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogUpdatedHandler<T> where T : class
{
    /// <summary>
    /// Called after a catalog entry has been updated.
    /// </summary>
    /// <param name="context">The context containing the updated entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task UpdatedAsync(UpdatedContext<T> context, CancellationToken cancellationToken = default);
}
