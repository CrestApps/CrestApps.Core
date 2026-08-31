#nullable enable
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// <see cref="IRealtimeTurnStore"/> that persists realtime turns as <see cref="AIChatSessionPrompt"/>
/// records, so a realtime conversation appears in an AI Chat session's history exactly like a text chat.
/// </summary>
public sealed class ChatSessionRealtimeTurnStore : IRealtimeTurnStore
{
    private readonly IAIChatSessionPromptStore _promptStore;

    public ChatSessionRealtimeTurnStore(IAIChatSessionPromptStore promptStore)
    {
        _promptStore = promptStore;
    }

    /// <inheritdoc />
    public ValueTask CreateUserTurnAsync(string sessionId, string text, DateTime createdUtc, CancellationToken cancellationToken)
    {
        return _promptStore.CreateAsync(new AIChatSessionPrompt
        {
            ItemId = UniqueId.GenerateId(),
            SessionId = sessionId,
            Role = ChatRole.User,
            Content = text,
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
        return _promptStore.CreateAsync(new AIChatSessionPrompt
        {
            ItemId = messageId,
            SessionId = sessionId,
            Role = ChatRole.Assistant,
            Title = title,
            Content = content,
            References = references,
            CreatedUtc = createdUtc,
        }, cancellationToken);
    }
}
