using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using CrestApps.Core.Models;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Services;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIChatSessionPromptStore : IAIChatSessionPromptStore
{
    private readonly ISession _session;
    private readonly string _collection;

    public YesSqlAIChatSessionPromptStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collection = options.Value.AICollectionName;
    }

    public async Task<IReadOnlyList<AIChatSessionPrompt>> GetPromptsAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var prompts = await _session.Query<AIChatSessionPrompt, AIChatSessionPromptIndex>(x => x.SessionId == sessionId, collection: _collection).ListAsync();

        return prompts.OrderBy(p => p.CreatedUtc).ToArray();
    }

    public async Task<int> DeleteAllPromptsAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var prompts = await _session.Query<AIChatSessionPrompt, AIChatSessionPromptIndex>(x => x.SessionId == sessionId, collection: _collection).ListAsync();
        var count = 0;

        foreach (var p in prompts)
        {
            _session.Delete(p, _collection);
            count++;
        }

        return count;
    }

    public async Task<int> CountAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        return await _session.Query<AIChatSessionPrompt, AIChatSessionPromptIndex>(x => x.SessionId == sessionId, collection: _collection).CountAsync();
    }

    public async ValueTask<AIChatSessionPrompt> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return await _session.Query<AIChatSessionPrompt, AIChatSessionPromptIndex>(x => x.ItemId == id, collection: _collection).FirstOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<AIChatSessionPrompt>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var items = await _session.Query<AIChatSessionPrompt, AIChatSessionPromptIndex>(x => x.ItemId.IsIn(ids), collection: _collection).ListAsync(cancellationToken);

        return items.ToArray();
    }

    public async ValueTask<IReadOnlyCollection<AIChatSessionPrompt>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _session.Query<AIChatSessionPrompt, AIChatSessionPromptIndex>(collection: _collection).ListAsync(cancellationToken);

        return items.ToArray();
    }

    public async ValueTask<PageResult<AIChatSessionPrompt>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        var query = _session.Query<AIChatSessionPrompt, AIChatSessionPromptIndex>(collection: _collection);
        var skip = (page - 1) * pageSize;

        return new PageResult<AIChatSessionPrompt>
        {
            Count = await query.CountAsync(cancellationToken),
            Entries = (await query.Skip(skip).Take(pageSize).ListAsync(cancellationToken)).ToArray(),
        };
    }

    public async ValueTask CreateAsync(AIChatSessionPrompt record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await _session.SaveAsync(record, _collection);
    }

    public async ValueTask UpdateAsync(AIChatSessionPrompt record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _session.SaveAsync(record, _collection);
    }

    public ValueTask<bool> DeleteAsync(AIChatSessionPrompt entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _session.Delete(entry, _collection);

        return ValueTask.FromResult(true);
    }
}
