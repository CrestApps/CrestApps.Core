using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AIMemory;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIMemoryStore : DocumentCatalog<AIMemoryEntry, AIMemoryEntryIndex>, IAIMemoryStore
{
    public YesSqlAIMemoryStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AIMemoryCollectionName)
    {
    }

    public async Task<int> CountByUserAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        return await Session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x => x.UserId == userId, collection: CollectionName)
            .CountAsync();
    }

    public async Task<AIMemoryEntry> FindByUserAndNameAsync(string userId, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return await Session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x =>
            x.UserId == userId && x.Name == name, collection: CollectionName).FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyCollection<AIMemoryEntry>> GetByUserAsync(string userId, int limit = 100)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        var items = await Session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x => x.UserId == userId, collection: CollectionName)
            .Take(limit)
            .ListAsync();

        return items.ToArray();
    }
}
