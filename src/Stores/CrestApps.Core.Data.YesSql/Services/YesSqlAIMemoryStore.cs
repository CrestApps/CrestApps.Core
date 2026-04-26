using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AIMemory;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIMemoryStore : DocumentCatalog<AIMemoryEntry, AIMemoryEntryIndex>, IAIMemoryStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAIMemoryStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlAIMemoryStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AIMemoryCollectionName)
    {
    }

    /// <summary>
    /// Counts by user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    public async Task<int> CountByUserAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        return await Session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x => x.UserId == userId, collection: CollectionName)
            .CountAsync();
    }

    /// <summary>
    /// Finds by user and name.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="name">The name.</param>
    public async Task<AIMemoryEntry> FindByUserAndNameAsync(string userId, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return await Session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x =>
            x.UserId == userId && x.Name == name, collection: CollectionName).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Gets by user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="limit">The limit.</param>
    public async Task<IReadOnlyCollection<AIMemoryEntry>> GetByUserAsync(string userId, int limit = 100)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        var items = await Session.Query<AIMemoryEntry, AIMemoryEntryIndex>(x => x.UserId == userId, collection: CollectionName)
            .Take(limit)
            .ListAsync();

        return items.ToArray();
    }
}
