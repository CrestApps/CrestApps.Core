using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;
using CrestApps.Core.Models;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Services;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlChatInteractionPromptStore : IChatInteractionPromptStore
{
    private readonly ISession _session;
    private readonly string _collection;

    public YesSqlChatInteractionPromptStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collection = options.Value.AICollectionName;
    }

    public async Task<IReadOnlyCollection<ChatInteractionPrompt>> GetPromptsAsync(string chatInteractionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatInteractionId);

        var prompts = await _session
            .Query<ChatInteractionPrompt, ChatInteractionPromptIndex>(x => x.ChatInteractionId == chatInteractionId, collection: _collection)
            .ListAsync();

        return prompts.OrderBy(p => p.CreatedUtc).ToArray();
    }

    public async Task<int> DeleteAllPromptsAsync(string chatInteractionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatInteractionId);

        var prompts = await _session
            .Query<ChatInteractionPrompt, ChatInteractionPromptIndex>(x => x.ChatInteractionId == chatInteractionId, collection: _collection)
            .ListAsync();

        var count = 0;

        foreach (var prompt in prompts)
        {
            _session.Delete(prompt, _collection);
            count++;
        }

        return count;
    }

    public async ValueTask<ChatInteractionPrompt> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return await _session
            .Query<ChatInteractionPrompt, ChatInteractionPromptIndex>(x => x.ItemId == id, collection: _collection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ChatInteractionPrompt>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var items = await _session
            .Query<ChatInteractionPrompt, ChatInteractionPromptIndex>(x => x.ItemId.IsIn(ids), collection: _collection)
            .ListAsync(cancellationToken);

        return items.ToArray();
    }

    public async ValueTask<IReadOnlyCollection<ChatInteractionPrompt>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _session
            .Query<ChatInteractionPrompt, ChatInteractionPromptIndex>(collection: _collection)
            .ListAsync(cancellationToken);

        return items.ToArray();
    }

    public async ValueTask<PageResult<ChatInteractionPrompt>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        var query = _session.Query<ChatInteractionPrompt, ChatInteractionPromptIndex>(collection: _collection);
        var skip = (page - 1) * pageSize;

        return new PageResult<ChatInteractionPrompt>
        {
            Count = await query.CountAsync(cancellationToken),
            Entries = (await query.Skip(skip).Take(pageSize).ListAsync(cancellationToken)).ToArray(),
        };
    }

    public async ValueTask CreateAsync(ChatInteractionPrompt record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await _session.SaveAsync(record, _collection);
    }

    public async ValueTask UpdateAsync(ChatInteractionPrompt record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _session.SaveAsync(record, _collection);
    }

    public ValueTask<bool> DeleteAsync(ChatInteractionPrompt entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _session.Delete(entry, _collection);

        return ValueTask.FromResult(true);
    }
}
