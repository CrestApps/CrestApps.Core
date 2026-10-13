namespace CrestApps.Core.AI.Animations.Storage;

/// <summary>
/// Keeps the videos the animation agent renders, each under the conversation it was made in, so the chat can play
/// them and they are removed with the conversation.
/// </summary>
public interface IAnimationStore
{
    /// <summary>
    /// Stores a rendered animation.
    /// </summary>
    /// <param name="owner">The conversation the animation belongs to.</param>
    /// <param name="content">The rendered video, its poster, and the script and settings it was made from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The identifier the animation is stored under.</returns>
    Task<string> SaveAsync(AnimationOwner owner, AnimationContent content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens one stored file of an animation for reading.
    /// </summary>
    /// <param name="owner">The conversation the animation belongs to.</param>
    /// <param name="animationId">The animation's identifier.</param>
    /// <param name="asset">Which of its files to open.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The file, or <see langword="null"/> when there is no such animation or file.</returns>
    Task<Stream> OpenReadAsync(AnimationOwner owner, string animationId, AnimationAsset asset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns what the most recent animation of a conversation was made from: its script and settings.
    /// </summary>
    /// <param name="owner">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The source, or <see langword="null"/> when the conversation has no animation.</returns>
    Task<AnimationSource> GetLatestSourceAsync(AnimationOwner owner, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every animation stored for a conversation.
    /// </summary>
    /// <param name="owner">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteAllAsync(AnimationOwner owner, CancellationToken cancellationToken = default);
}
