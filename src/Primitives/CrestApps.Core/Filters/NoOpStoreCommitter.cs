using CrestApps.Core.Services;

namespace CrestApps.Core.Filters;

/// <summary>
/// A no-op <see cref="IStoreCommitter"/> registered as a safety net by
/// <c>AddCrestAppsCore</c> so that <see cref="StoreCommitterActionFilter"/>,
/// <see cref="StoreCommitterHubFilter"/>, and
/// <see cref="StoreCommitterEndpointFilter"/> can be wired even when no
/// store package supplies a real committer (for example, unit tests or
/// hosts that use a custom persistence layer).
/// Real store packages overwrite this registration.
/// </summary>
internal sealed class NoOpStoreCommitter : IStoreCommitter
{
    public ValueTask CommitAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
