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

    public ValueTask<SearchIndexProfile> FindByNameAsync(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return _store.FindByNameAsync(name);
    }

    public Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);

        return _store.GetByTypeAsync(type);
    }

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

    public async Task ResetAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        foreach (var handler in _handlers)
        {
            await handler.ResetAsync(profile, cancellationToken);
        }
    }

    public async Task SynchronizeAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        foreach (var handler in _handlers)
        {
            await handler.SynchronizedAsync(profile, cancellationToken);
        }
    }
}
