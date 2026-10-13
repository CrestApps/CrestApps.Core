using CrestApps.Core.AI.Animations.Storage;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Animations.Handlers;

/// <summary>
/// Deletes a chat interaction's animations when its history is cleared, so no video outlives the messages that
/// showed it.
/// </summary>
internal sealed class AnimationHistoryClearedHandler : IChatInteractionHistoryHandler
{
    private readonly IAnimationStore _animationStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationHistoryClearedHandler"/> class.
    /// </summary>
    /// <param name="animationStore">The animation store.</param>
    public AnimationHistoryClearedHandler(IAnimationStore animationStore)
    {
        _animationStore = animationStore;
    }

    /// <summary>
    /// Deletes the animations.
    /// </summary>
    /// <param name="interaction">The chat interaction whose history was cleared.</param>
    /// <param name="clearedPrompts">The prompts that were removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task HistoryClearedAsync(
        ChatInteraction interaction,
        IReadOnlyCollection<ChatInteractionPrompt> clearedPrompts,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(interaction?.ItemId))
        {
            return Task.CompletedTask;
        }

        return _animationStore.DeleteAllAsync(
            new AnimationOwner(AIReferenceTypes.Document.ChatInteraction, interaction.ItemId),
            cancellationToken);
    }
}
