namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Publishes tabular workspace invalidations. Distributed hosts can replace this service to
/// fan out invalidations through a shared backplane.
/// </summary>
public interface ITabularWorkspaceInvalidationPublisher
{
    /// <summary>
    /// Publishes the invalidation.
    /// </summary>
    /// <param name="invalidation">The invalidation to publish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task PublishAsync(
        TabularWorkspaceInvalidation invalidation,
        CancellationToken cancellationToken = default);
}
