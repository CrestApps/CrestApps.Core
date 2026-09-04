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
    private readonly PendingUserTurnTracker<AIChatSessionPrompt> _pendingUserTurns = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatSessionRealtimeTurnStore"/> class.
    /// </summary>
    /// <param name="promptStore">The chat session prompt store.</param>
    public ChatSessionRealtimeTurnStore(IAIChatSessionPromptStore promptStore)
    {
        _promptStore = promptStore;
    }

    /// <inheritdoc />
    public ValueTask CreateUserTurnAsync(string sessionId, string turnId, string text, DateTime createdUtc, CancellationToken cancellationToken)
    {
        var prompt = new AIChatSessionPrompt
        {
            ItemId = UniqueId.GenerateId(),
            SessionId = sessionId,
            Role = ChatRole.User,
            Content = text,
            CreatedUtc = createdUtc,
        };

        _pendingUserTurns.Track(turnId, prompt);

        return _promptStore.CreateAsync(prompt, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask UpdateUserTurnAsync(string sessionId, string turnId, string text, CancellationToken cancellationToken)
    {
        var prompt = _pendingUserTurns.Take(turnId);

        if (prompt is null)
        {
            return ValueTask.CompletedTask;
        }

        prompt.Content = text;

        return _promptStore.UpdateAsync(prompt, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DeleteUserTurnAsync(string sessionId, string turnId, CancellationToken cancellationToken)
    {
        var prompt = _pendingUserTurns.Take(turnId);

        if (prompt is null)
        {
            return ValueTask.CompletedTask;
        }

        return new ValueTask(_promptStore.DeleteAsync(prompt, cancellationToken).AsTask());
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
