using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlChatInteractionPromptStore : DocumentCatalog<ChatInteractionPrompt, ChatInteractionPromptIndex>, IChatInteractionPromptStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlChatInteractionPromptStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlChatInteractionPromptStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AICollectionName)
    {
    }

    /// <summary>
    /// Gets prompts.
    /// </summary>
    /// <param name="chatInteractionId">The chat interaction id.</param>
    public async Task<IReadOnlyCollection<ChatInteractionPrompt>> GetPromptsAsync(string chatInteractionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatInteractionId);

        var prompts = await Session
            .Query<ChatInteractionPrompt, ChatInteractionPromptIndex>(x => x.ChatInteractionId == chatInteractionId, collection: CollectionName)
            .ListAsync();

return prompts.OrderBy(p => p.CreatedUtc).ToArray();
    }

    /// <summary>
    /// Deletes all prompts.
    /// </summary>
    /// <param name="chatInteractionId">The chat interaction id.</param>
    public async Task<int> DeleteAllPromptsAsync(string chatInteractionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatInteractionId);

        var prompts = await Session
            .Query<ChatInteractionPrompt, ChatInteractionPromptIndex>(x => x.ChatInteractionId == chatInteractionId, collection: CollectionName)
            .ListAsync();

        var count = 0;

        foreach (var prompt in prompts)
        {
            Session.Delete(prompt, CollectionName);
            count++;
        }

        return count;
    }
}
