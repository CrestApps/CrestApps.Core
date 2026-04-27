using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// Fallback in-memory no-op store used when a host has not registered a persistent
/// <see cref="IAIProfileStore"/> implementation yet.
/// </summary>
public sealed class NullAIProfileStore : IAIProfileStore
{
    /// <summary>
    /// Finds an AI profile by identifier.
    /// </summary>
    /// <param name="id">The identifier to look up.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public ValueTask<AIProfile> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return ValueTask.FromResult<AIProfile>(default);
    }

    /// <summary>
    /// Gets all stored AI profiles.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public ValueTask<IReadOnlyCollection<AIProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyCollection<AIProfile>>([]);
    }

    /// <summary>
    /// Gets AI profiles by identifier.
    /// </summary>
    /// <param name="ids">The identifiers to load.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public ValueTask<IReadOnlyCollection<AIProfile>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return ValueTask.FromResult<IReadOnlyCollection<AIProfile>>([]);
    }

    /// <summary>
    /// Pages AI profiles.
    /// </summary>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="context">The query context.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public ValueTask<PageResult<AIProfile>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        ArgumentNullException.ThrowIfNull(context);

        return ValueTask.FromResult(new PageResult<AIProfile>
        {
            Count = 0,
            Entries = [],
        });
    }

    /// <summary>
    /// Deletes an AI profile.
    /// </summary>
    /// <param name="entry">The entry to delete.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public ValueTask<bool> DeleteAsync(AIProfile entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return ValueTask.FromResult(false);
    }

    /// <summary>
    /// Creates an AI profile.
    /// </summary>
    /// <param name="entry">The entry to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public ValueTask CreateAsync(AIProfile entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Updates an AI profile.
    /// </summary>
    /// <param name="entry">The entry to update.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public ValueTask UpdateAsync(AIProfile entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Finds an AI profile by name.
    /// </summary>
    /// <param name="name">The unique name to look up.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public ValueTask<AIProfile> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return ValueTask.FromResult<AIProfile>(default);
    }

    /// <summary>
    /// Gets AI profiles by profile type.
    /// </summary>
    /// <param name="type">The profile type to load.</param>
    public ValueTask<IReadOnlyCollection<AIProfile>> GetByTypeAsync(AIProfileType type)
    {
        return ValueTask.FromResult<IReadOnlyCollection<AIProfile>>([]);
    }
}
