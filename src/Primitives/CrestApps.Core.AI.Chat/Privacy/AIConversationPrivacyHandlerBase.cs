using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Privacy;

/// <summary>
/// A base for <see cref="IAIConversationPrivacyHandler"/> implementations whose methods do nothing until overridden.
/// </summary>
public abstract class AIConversationPrivacyHandlerBase : IAIConversationPrivacyHandler
{
    /// <summary>
    /// Does nothing until overridden.
    /// </summary>
    /// <param name="context">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    public virtual Task CollectingAsync(AIConversationPrivacyContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Does nothing until overridden.
    /// </summary>
    /// <param name="context">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    public virtual Task ApplyingAsync(AIConversationPrivacyContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Does nothing until overridden.
    /// </summary>
    /// <param name="session">The chat session being deleted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    public virtual Task DeletingAsync(AIChatSession session, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
