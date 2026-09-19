using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.FileSources;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql-backed <see cref="IIngestionItemStateStore"/>.
/// </summary>
public sealed class YesSqlIngestionItemStateStore : DocumentCatalog<IngestionItemState, IngestionItemStateIndex>, IIngestionItemStateStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlIngestionItemStateStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlIngestionItemStateStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AICollectionName)
    {
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<IngestionItemState>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return (await Session.Query<IngestionItemState, IngestionItemStateIndex>(x => x.Source == source, collection: CollectionName).ListAsync(cancellationToken)).ToArray();
    }

    /// <inheritdoc />
    public async Task DeleteBySourceIdAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);

        var states = await Session.Query<IngestionItemState, IngestionItemStateIndex>(x => x.Source == sourceId, collection: CollectionName).ListAsync(cancellationToken);

        foreach (var state in states)
        {
            Session.Delete(state, CollectionName);
        }
    }

    /// <inheritdoc />
    public async Task DeleteByItemKeysAsync(string sourceId, IEnumerable<string> itemKeys, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentNullException.ThrowIfNull(itemKeys);

        var keys = new HashSet<string>(itemKeys.Where(key => !string.IsNullOrWhiteSpace(key)), StringComparer.OrdinalIgnoreCase);

        if (keys.Count == 0)
        {
            return;
        }

        var states = await Session.Query<IngestionItemState, IngestionItemStateIndex>(x => x.Source == sourceId, collection: CollectionName).ListAsync(cancellationToken);

        foreach (var state in states)
        {
            if (keys.Contains(state.ItemKey))
            {
                Session.Delete(state, CollectionName);
            }
        }
    }
}
