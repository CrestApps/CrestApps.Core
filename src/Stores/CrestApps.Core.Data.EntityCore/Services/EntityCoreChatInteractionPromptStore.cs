using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreChatInteractionPromptStore : DocumentCatalog<ChatInteractionPrompt>, IChatInteractionPromptStore
{
    public EntityCoreChatInteractionPromptStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<ChatInteractionPrompt>> logger = null)
        : base(dbContext, logger)
    {
    }

    public async Task<IReadOnlyCollection<ChatInteractionPrompt>> GetPromptsAsync(string chatInteractionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatInteractionId);

        var records = await GetReadQuery()
            .Where(x => x.ChatInteractionId == chatInteractionId)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();

        return records
            .Select(CatalogRecordFactory.Materialize<ChatInteractionPrompt>)
            .ToArray();
    }

    public async Task<int> DeleteAllPromptsAsync(string chatInteractionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatInteractionId);

        var records = await GetTrackedQuery()
            .Where(x => x.ChatInteractionId == chatInteractionId)
            .ToListAsync();

        if (records.Count == 0)
        {
            return 0;
        }

        DbContext.CatalogRecords.RemoveRange(records);

        return records.Count;
    }
}
