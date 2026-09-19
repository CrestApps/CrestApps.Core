using System.Text.Json.Nodes;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Keeps per-item ingestion state in memory so a run can be exercised without a database.
/// </summary>
internal sealed class InMemoryIngestionItemStateStore : IIngestionItemStateStore
{
    private readonly List<IngestionItemState> _entries = [];

    /// <summary>
    /// Gets everything currently stored.
    /// </summary>
    public IReadOnlyList<IngestionItemState> All => _entries;

    public ValueTask CreateAsync(IngestionItemState entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(IngestionItemState entry, CancellationToken cancellationToken = default)
    {
        var index = _entries.FindIndex(item => item.ItemId == entry.ItemId);

        if (index >= 0)
        {
            _entries[index] = entry;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteAsync(IngestionItemState entry, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_entries.RemoveAll(item => item.ItemId == entry.ItemId) > 0);
    }

    public ValueTask<IngestionItemState> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_entries.FirstOrDefault(item => item.ItemId == id));
    }

    public ValueTask<IReadOnlyCollection<IngestionItemState>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyCollection<IngestionItemState>>(_entries.ToArray());
    }

    public ValueTask<IReadOnlyCollection<IngestionItemState>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<string>(ids, StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlyCollection<IngestionItemState>>(
            _entries.Where(item => wanted.Contains(item.ItemId)).ToArray());
    }

    public ValueTask<IReadOnlyCollection<IngestionItemState>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyCollection<IngestionItemState>>(
            _entries.Where(item => item.Source == source).ToArray());
    }

    public static ValueTask<IngestionItemState> NewAsync(string source, JsonNode data = null, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new IngestionItemState
        {
            ItemId = Guid.NewGuid().ToString("N"),
            Source = source,
        });
    }

    public ValueTask<PageResult<IngestionItemState>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        var items = _entries.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return ValueTask.FromResult(new PageResult<IngestionItemState>
        {
            Count = _entries.Count,
            Entries = items,
        });
    }

    public Task DeleteBySourceIdAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        _entries.RemoveAll(item => item.Source == sourceId);

        return Task.CompletedTask;
    }

    public Task DeleteByItemKeysAsync(string sourceId, IEnumerable<string> itemKeys, CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<string>(itemKeys, StringComparer.Ordinal);

        _entries.RemoveAll(item => item.Source == sourceId && wanted.Contains(item.ItemKey));

        return Task.CompletedTask;
    }
}
