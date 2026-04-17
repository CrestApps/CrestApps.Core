using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.Indexing;
using CrestApps.Core.Models;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Services;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIDocumentStore : IAIDocumentStore
{
    private readonly ISession _session;
    private readonly string _collection;

    public YesSqlAIDocumentStore(ISession session, IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collection = options.Value.AIDocsCollectionName;
    }

    public async Task<IReadOnlyCollection<AIDocument>> GetDocumentsAsync(string referenceId, string referenceType)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceId);
        ArgumentException.ThrowIfNullOrEmpty(referenceType);

        var docs = await _session.Query<AIDocument, AIDocumentIndex>(x =>
        x.ReferenceId == referenceId && x.ReferenceType == referenceType, collection: _collection).ListAsync();

        return docs.ToArray();
    }

    public async ValueTask<AIDocument> FindByIdAsync(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return await _session.Query<AIDocument, AIDocumentIndex>(x => x.ItemId == id, collection: _collection).FirstOrDefaultAsync();
    }

    public async ValueTask<IReadOnlyCollection<AIDocument>> GetAsync(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var items = await _session.Query<AIDocument, AIDocumentIndex>(x => x.ItemId.IsIn(ids), collection: _collection).ListAsync();

        return items.ToArray();
    }

    public async ValueTask<IReadOnlyCollection<AIDocument>> GetAllAsync()
    {
        var items = await _session.Query<AIDocument, AIDocumentIndex>(collection: _collection).ListAsync();

        return items.ToArray();
    }

    public async ValueTask<PageResult<AIDocument>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        var query = _session.Query<AIDocument, AIDocumentIndex>(collection: _collection);
        var skip = (page - 1) * pageSize;

        return new PageResult<AIDocument>
        {
            Count = await query.CountAsync(),
            Entries = (await query.Skip(skip).Take(pageSize).ListAsync()).ToArray(),
        };
    }

    public async ValueTask CreateAsync(AIDocument record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await _session.SaveAsync(record, _collection);
    }

    public async ValueTask UpdateAsync(AIDocument record)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _session.SaveAsync(record, _collection);
    }

    public ValueTask<bool> DeleteAsync(AIDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _session.Delete(entry, _collection);

        return ValueTask.FromResult(true);
    }

}
