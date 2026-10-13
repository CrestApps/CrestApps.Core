using CrestApps.Core.AI.Animations.Storage;
using CrestApps.Core.AI.Documents;

namespace CrestApps.Core.AI.Animations.Handlers;

/// <summary>
/// Deletes a conversation's animations when the conversation is deleted.
/// </summary>
internal sealed class AnimationWorkspaceCleanupHandler : IConversationWorkspaceCleanupHandler
{
    private readonly IAnimationStore _animationStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationWorkspaceCleanupHandler"/> class.
    /// </summary>
    /// <param name="animationStore">The animation store.</param>
    public AnimationWorkspaceCleanupHandler(IAnimationStore animationStore)
    {
        _animationStore = animationStore;
    }

    /// <summary>
    /// Deletes the animations kept for the conversation.
    /// </summary>
    /// <param name="referenceId">The conversation identifier.</param>
    /// <param name="referenceType">The conversation reference type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task CleanupAsync(string referenceId, string referenceType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(referenceId) || string.IsNullOrEmpty(referenceType))
        {
            return Task.CompletedTask;
        }

        return _animationStore.DeleteAllAsync(new AnimationOwner(referenceType, referenceId), cancellationToken);
    }
}
