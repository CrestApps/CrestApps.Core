using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Represents the search Index Profile Manager.
/// </summary>
public sealed class SearchIndexProfileManager : CatalogManager<SearchIndexProfile>, ISearchIndexProfileManager
{
    private readonly ISearchIndexProfileStore _store;
    private readonly IEnumerable<IIndexProfileHandler> _handlers;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexProfileManager"/> class.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="logger">The logger.</param>
    public SearchIndexProfileManager(
        ISearchIndexProfileStore store,
        IEnumerable<IIndexProfileHandler> handlers,
        ILogger<SearchIndexProfileManager> logger)
        : base(store, handlers, logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _handlers = handlers;
    }

    /// <summary>
    /// Finds by name.
    /// </summary>
    /// <param name="name">The name.</param>
    public ValueTask<SearchIndexProfile> FindByNameAsync(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return _store.FindByNameAsync(name);
    }

    /// <summary>
    /// Gets by type.
    /// </summary>
    /// <param name="type">The type.</param>
    public Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);

        return _store.GetByTypeAsync(type);
    }

    /// <summary>
    /// Gets fields.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyCollection<SearchIndexField>> GetFieldsAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        foreach (var handler in _handlers)
        {
            var fields = await handler.GetFieldsAsync(profile, cancellationToken);
            if (fields != null)
            {
                return fields;
            }
        }

        return null;
    }

    /// <summary>
    /// Resets the operation.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task ResetAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        foreach (var handler in _handlers)
        {
            await handler.ResetAsync(profile, cancellationToken);
        }
    }

    /// <summary>
    /// Synchronizes the operation.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SynchronizeAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        foreach (var handler in _handlers)
        {
            await handler.SynchronizedAsync(profile, cancellationToken);
        }
    }
}
