using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIChatSessionPromptStore : DocumentCatalog<AIChatSessionPrompt, AIChatSessionPromptIndex>, IAIChatSessionPromptStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAIChatSessionPromptStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlAIChatSessionPromptStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AICollectionName)
    {
    }

    /// <summary>
    /// Gets prompts.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public async Task<IReadOnlyList<AIChatSessionPrompt>> GetPromptsAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var prompts = await Session.Query<AIChatSessionPrompt, AIChatSessionPromptIndex>(x => x.SessionId == sessionId, collection: CollectionName).ListAsync();

return prompts.OrderBy(p => p.CreatedUtc).ToArray();
    }

    /// <summary>
    /// Deletes all prompts.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public async Task<int> DeleteAllPromptsAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var prompts = await Session.Query<AIChatSessionPrompt, AIChatSessionPromptIndex>(x => x.SessionId == sessionId, collection: CollectionName).ListAsync();
        var count = 0;

        foreach (var p in prompts)
        {
            Session.Delete(p, CollectionName);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Counts the operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public async Task<int> CountAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

return await Session.Query<AIChatSessionPrompt, AIChatSessionPromptIndex>(x => x.SessionId == sessionId, collection: CollectionName).CountAsync();
    }
}
