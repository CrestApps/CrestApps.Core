using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.Indexing;
using CrestApps.Core.Models;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Services;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIDocumentChunkStore : IAIDocumentChunkStore
{
    private readonly ISession _session;
    private readonly string _collection;

    public YesSqlAIDocumentChunkStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collection = options.Value.AIDocsCollectionName;
    }

    public async Task<IReadOnlyCollection<AIDocumentChunk>> GetChunksByAIDocumentIdAsync(string documentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        var chunks = await _session.Query<AIDocumentChunk, AIDocumentChunkIndex>(x =>
        x.AIDocumentId == documentId, collection: _collection).ListAsync();

        return chunks.ToArray();
    }

    public async Task<IReadOnlyCollection<AIDocumentChunk>> GetChunksByReferenceAsync(string referenceId, string referenceType)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceId);
        ArgumentException.ThrowIfNullOrEmpty(referenceType);

        var chunks = await _session.Query<AIDocumentChunk, AIDocumentChunkIndex>(x =>
        x.ReferenceId == referenceId && x.ReferenceType == referenceType, collection: _collection).ListAsync();

        return chunks.ToArray();
    }

    public async Task DeleteByDocumentIdAsync(string documentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        var chunks = await _session.Query<AIDocumentChunk, AIDocumentChunkIndex>(x =>
        x.AIDocumentId == documentId, collection: _collection).ListAsync();

        foreach (var chunk in chunks)
        {
            _session.Delete(chunk, _collection);
        }
    }

    public async ValueTask<AIDocumentChunk> FindByIdAsync(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return await _session.Query<AIDocumentChunk, AIDocumentChunkIndex>(x => x.ItemId == id, collection: _collection).FirstOrDefaultAsync();
    }

    public async ValueTask<IReadOnlyCollection<AIDocumentChunk>> GetAsync(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var items = await _session.Query<AIDocumentChunk, AIDocumentChunkIndex>(x => x.ItemId.IsIn(ids), collection: _collection).ListAsync();

        return items.ToArray();
    }

    public async ValueTask<IReadOnlyCollection<AIDocumentChunk>> GetAllAsync()
    {
        var items = await _session.Query<AIDocumentChunk, AIDocumentChunkIndex>(collection: _collection).ListAsync();

        return items.ToArray();
    }

    public async ValueTask<PageResult<AIDocumentChunk>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        var query = _session.Query<AIDocumentChunk, AIDocumentChunkIndex>(collection: _collection);
        var skip = (page - 1) * pageSize;

        return new PageResult<AIDocumentChunk>
        {
            Count = await query.CountAsync(),
            Entries = (await query.Skip(skip).Take(pageSize).ListAsync()).ToArray(),
        };
    }

    public async ValueTask CreateAsync(AIDocumentChunk record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await _session.SaveAsync(record, _collection);
    }

    public async ValueTask UpdateAsync(AIDocumentChunk record)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _session.SaveAsync(record, _collection);
    }

    public ValueTask<bool> DeleteAsync(AIDocumentChunk entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _session.Delete(entry, _collection);

        return ValueTask.FromResult(true);
    }
}
