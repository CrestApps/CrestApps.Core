using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AIMemory;
using CrestApps.Core.Models;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Services;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIMemoryStore : IAIMemoryStore
{
    private readonly ISession _session;
    private readonly string _collection;

    public YesSqlAIMemoryStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collection = options.Value.AIMemoryCollectionName;
    }

    public async Task<int> CountByUserAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        return await _session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x => x.UserId == userId, collection: _collection)
            .CountAsync();
    }

    public async Task<AIMemoryEntry> FindByUserAndNameAsync(string userId, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return await _session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x =>
        x.UserId == userId && x.Name == name, collection: _collection).FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyCollection<AIMemoryEntry>> GetByUserAsync(string userId, int limit = 100)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        var items = await _session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x => x.UserId == userId, collection: _collection)
            .Take(limit)
            .ListAsync();

        return items.ToArray();
    }

    public async ValueTask<AIMemoryEntry> FindByIdAsync(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return await _session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x => x.ItemId == id, collection: _collection)
            .FirstOrDefaultAsync();
    }

    public async ValueTask<IReadOnlyCollection<AIMemoryEntry>> GetAsync(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var items = await _session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x => x.ItemId.IsIn(ids), collection: _collection)
            .ListAsync();

        return items.ToArray();
    }

    public async ValueTask<IReadOnlyCollection<AIMemoryEntry>> GetAllAsync()
    {
        var items = await _session.Query<AIMemoryEntry, AIMemoryEntryIndex>(collection: _collection).ListAsync();

        return items.ToArray();
    }

    public async ValueTask<PageResult<AIMemoryEntry>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        var query = _session.Query<AIMemoryEntry, AIMemoryEntryIndex>(collection: _collection);
        var skip = (page - 1) * pageSize;

        return new PageResult<AIMemoryEntry>
        {
            Count = await query.CountAsync(),
            Entries = (await query.Skip(skip).Take(pageSize).ListAsync()).ToArray(),
        };
    }

    public async ValueTask CreateAsync(AIMemoryEntry record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await _session.SaveAsync(record, _collection);
    }

    public async ValueTask UpdateAsync(AIMemoryEntry record)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _session.SaveAsync(record, _collection);
    }

    public ValueTask<bool> DeleteAsync(AIMemoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _session.Delete(entry, _collection);

        return ValueTask.FromResult(true);
    }
}
