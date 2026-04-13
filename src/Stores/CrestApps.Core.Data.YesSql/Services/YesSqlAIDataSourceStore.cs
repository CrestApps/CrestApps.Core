using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.DataSources;
using CrestApps.Core.Models;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Services;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIDataSourceStore : IAIDataSourceStore
{
    private readonly ISession _session;
    private readonly string _collection;

    public YesSqlAIDataSourceStore(ISession session, IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collection = options.Value.AIDocsCollectionName;
    }

    public async ValueTask<AIDataSource> FindByIdAsync(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return await _session.Query<AIDataSource, AIDataSourceIndex>(x => x.ItemId == id, collection: _collection)
            .FirstOrDefaultAsync();
    }

    public async ValueTask<IReadOnlyCollection<AIDataSource>> GetAsync(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var items = await _session.Query<AIDataSource, AIDataSourceIndex>(x => x.ItemId.IsIn(ids), collection: _collection)
            .ListAsync();

        return items.ToArray();
    }

    public async ValueTask<IReadOnlyCollection<AIDataSource>> GetAllAsync()
    {
        var items = await _session.Query<AIDataSource, AIDataSourceIndex>(collection: _collection).ListAsync();

        return items.ToArray();
    }

    public async ValueTask<PageResult<AIDataSource>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        var query = _session.Query<AIDataSource, AIDataSourceIndex>(collection: _collection);
        var skip = (page - 1) * pageSize;

        return new PageResult<AIDataSource>
        {
            Count = await query.CountAsync(),
            Entries = (await query.Skip(skip).Take(pageSize).ListAsync()).ToArray(),
        };
    }

    public async ValueTask CreateAsync(AIDataSource record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await _session.SaveAsync(record, _collection);
    }

    public async ValueTask UpdateAsync(AIDataSource record)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _session.SaveAsync(record, _collection);
    }

    public ValueTask<bool> DeleteAsync(AIDataSource entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _session.Delete(entry, _collection);

        return ValueTask.FromResult(true);
    }

}
