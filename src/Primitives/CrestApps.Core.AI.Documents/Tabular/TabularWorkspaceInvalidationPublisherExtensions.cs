namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Provides helper methods for publishing tabular workspace invalidations.
/// </summary>
public static class TabularWorkspaceInvalidationPublisherExtensions
{
    /// <summary>
    /// Publishes an invalidation through every registered publisher.
    /// </summary>
    /// <param name="publishers">The publishers.</param>
    /// <param name="invalidation">The invalidation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task PublishAllAsync(
        this IEnumerable<ITabularWorkspaceInvalidationPublisher> publishers,
        TabularWorkspaceInvalidation invalidation,
        CancellationToken cancellationToken = default)
    {
        if (publishers is null)
        {
            return;
        }

        foreach (var publisher in publishers)
        {
            await publisher.PublishAsync(invalidation, cancellationToken);
        }
    }
}
