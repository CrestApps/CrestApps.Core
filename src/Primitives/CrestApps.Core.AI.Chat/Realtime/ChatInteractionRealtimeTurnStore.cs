#nullable enable
using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// <see cref="IRealtimeTurnStore"/> that persists realtime turns as <see cref="ChatInteractionPrompt"/>
/// records, so a realtime conversation appears in a chat interaction's history exactly like a text chat.
/// </summary>
public sealed class ChatInteractionRealtimeTurnStore : IRealtimeTurnStore
{
    private readonly IChatInteractionPromptStore _promptStore;

    public ChatInteractionRealtimeTurnStore(IChatInteractionPromptStore promptStore)
    {
        _promptStore = promptStore;
    }

    /// <inheritdoc />
    public ValueTask CreateUserTurnAsync(string sessionId, string text, DateTime createdUtc, CancellationToken cancellationToken)
    {
        return _promptStore.CreateAsync(new ChatInteractionPrompt
        {
            ItemId = UniqueId.GenerateId(),
            ChatInteractionId = sessionId,
            Role = ChatRole.User,
            Text = text,
            CreatedUtc = createdUtc,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask CreateAssistantTurnAsync(
        string sessionId,
        string messageId,
        string content,
        string? title,
        Dictionary<string, AICompletionReference>? references,
        DateTime createdUtc,
        CancellationToken cancellationToken)
    {
        return _promptStore.CreateAsync(new ChatInteractionPrompt
        {
            ItemId = messageId,
            ChatInteractionId = sessionId,
            Role = ChatRole.Assistant,
            Title = title,
            Text = content,
            References = references,
            CreatedUtc = createdUtc,
        }, cancellationToken);
    }
}
