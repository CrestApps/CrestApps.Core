using CrestApps.Core.Data.YesSql.Indexes.Indexing;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Services;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlSearchIndexProfileStore : ISearchIndexProfileStore
{
    private readonly ISession _session;
    private readonly string _collection;

    public YesSqlSearchIndexProfileStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collection = options.Value.DefaultCollectionName;
    }

    public async ValueTask<SearchIndexProfile> FindByNameAsync(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return await _session.Query<SearchIndexProfile, SearchIndexProfileIndex>(x => x.Name == name, collection: _collection)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var items = await _session.Query<SearchIndexProfile, SearchIndexProfileIndex>(x => x.Type == type, collection: _collection)
            .ListAsync();

        return items.ToArray();
    }

    public async ValueTask<SearchIndexProfile> FindByIdAsync(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return await _session.Query<SearchIndexProfile, SearchIndexProfileIndex>(x => x.ItemId == id, collection: _collection)
            .FirstOrDefaultAsync();
    }

    public async ValueTask<IReadOnlyCollection<SearchIndexProfile>> GetAsync(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var items = await _session.Query<SearchIndexProfile, SearchIndexProfileIndex>(x => x.ItemId.IsIn(ids), collection: _collection)
            .ListAsync();

        return items.ToArray();
    }

    public async ValueTask<IReadOnlyCollection<SearchIndexProfile>> GetAllAsync()
    {
        var items = await _session.Query<SearchIndexProfile, SearchIndexProfileIndex>(collection: _collection).ListAsync();

        return items.ToArray();
    }

    public async ValueTask<PageResult<SearchIndexProfile>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        var query = _session.Query<SearchIndexProfile, SearchIndexProfileIndex>(collection: _collection);
        var skip = (page - 1) * pageSize;

        return new PageResult<SearchIndexProfile>
        {
            Count = await query.CountAsync(),
            Entries = (await query.Skip(skip).Take(pageSize).ListAsync()).ToArray(),
        };
    }

    public async ValueTask CreateAsync(SearchIndexProfile record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await _session.SaveAsync(record, _collection);
    }

    public async ValueTask UpdateAsync(SearchIndexProfile record)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _session.SaveAsync(record, _collection);
    }

    public ValueTask<bool> DeleteAsync(SearchIndexProfile entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _session.Delete(entry, _collection);

        return ValueTask.FromResult(true);
    }
}
