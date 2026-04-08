namespace CrestApps.Core.Services;

/// <summary>
/// Represents a commit boundary for the underlying data store.
/// Implementations flush all staged writes (e.g., <c>ISession.SaveChangesAsync</c> for YesSql)
/// to durable storage. The framework registers this interface for each first-party store
/// package and calls it automatically via <see cref="Filters.StoreCommitterActionFilter"/>,
/// <see cref="Filters.StoreCommitterEndpointFilter"/>, and
/// <see cref="Filters.StoreCommitterHubFilter"/>. SignalR hub implementations that
/// perform fire-and-forget operations must call <see cref="CommitAsync"/> explicitly
/// from within the async work lambda.
/// </summary>
public interface IStoreCommitter
{
    ValueTask CommitAsync(CancellationToken cancellationToken = default);
}
