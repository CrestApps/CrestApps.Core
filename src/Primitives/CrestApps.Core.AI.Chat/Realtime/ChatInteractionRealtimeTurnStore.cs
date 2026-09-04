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
    private readonly PendingUserTurnTracker<ChatInteractionPrompt> _pendingUserTurns = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatInteractionRealtimeTurnStore"/> class.
    /// </summary>
    /// <param name="promptStore">The chat interaction prompt store.</param>
    public ChatInteractionRealtimeTurnStore(IChatInteractionPromptStore promptStore)
    {
        _promptStore = promptStore;
    }

    /// <inheritdoc />
    public ValueTask CreateUserTurnAsync(string sessionId, string turnId, string text, DateTime createdUtc, CancellationToken cancellationToken)
    {
        var prompt = new ChatInteractionPrompt
        {
            ItemId = UniqueId.GenerateId(),
            ChatInteractionId = sessionId,
            Role = ChatRole.User,
            Text = text,
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

        prompt.Text = text;

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
